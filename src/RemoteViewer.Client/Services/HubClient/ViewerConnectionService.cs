using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Viewer;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

public sealed class ViewerConnectionService : IViewerServiceImpl, IDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<ViewerConnectionService> _logger;
    private readonly IWindowsKeyBlockerService _windowsKeyBlockerService;
    private readonly ConcurrentDictionary<ushort, object?> _pressedKeys = new();

    private readonly Lock _displayLock = new();
    private ImmutableList<DisplayInfo> _availableDisplays = [];
    private string? _currentDisplayId;

    private bool _disposed;

    public ViewerConnectionService(
        Connection connection,
        IWindowsKeyBlockerService windowsKeyBlockerService,
        ILogger<ViewerConnectionService> logger)
    {
        this._connection = connection;
        this._windowsKeyBlockerService = windowsKeyBlockerService;
        this._logger = logger;

        this._windowsKeyBlockerService.ShortcutIntercepted += this.WindowsKeyBlocker_ShortcutIntercepted;
        this._connection.ConnectionPropertiesChanged += this.Connection_ConnectionPropertiesChanged;
    }

    public bool IsInputEnabled { get; set; } = true;

    public event EventHandler<FrameReceivedEventArgs>? FrameReady;
    public event EventHandler? AvailableDisplaysChanged;
    public event EventHandler? CurrentDisplayChanged;

    public ImmutableList<DisplayInfo> AvailableDisplays
    {
        get
        {
            using (this._displayLock.EnterScope())
                return this._availableDisplays;
        }
    }

    public DisplayInfo? CurrentDisplay
    {
        get
        {
            using (this._displayLock.EnterScope())
            {
                if (this._currentDisplayId is null)
                    return null;
                return this._availableDisplays.FirstOrDefault(d => d.Id == this._currentDisplayId);
            }
        }
    }

    private void Connection_ConnectionPropertiesChanged(object? sender, EventArgs e)
    {
        var props = this._connection.ConnectionProperties;
        bool changed;

        using (this._displayLock.EnterScope())
        {
            var newDisplays = props.AvailableDisplays.ToImmutableList();
            changed = !this._availableDisplays.SequenceEqual(newDisplays);
            if (changed)
            {
                this._availableDisplays = newDisplays;
            }
        }

        if (changed)
        {
            this.AvailableDisplaysChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ImmutableList<DisplayInfo> GetAdjacentDisplays(NavigationDirection direction)
    {
        using (this._displayLock.EnterScope())
        {
            if (this._currentDisplayId is null || this._availableDisplays.Count == 0)
                return [];

            var current = this._availableDisplays.FirstOrDefault(d => d.Id == this._currentDisplayId);
            if (current is null)
                return [];

            var result = ImmutableList.CreateBuilder<DisplayInfo>();

            foreach (var candidate in this._availableDisplays)
            {
                if (candidate.Id == current.Id)
                    continue;

                bool isAdjacent;
                bool hasOverlap;

                switch (direction)
                {
                    case NavigationDirection.Left:
                        isAdjacent = current.Left - candidate.Right is 0 or 1;
                        hasOverlap = !(candidate.Bottom <= current.Top || candidate.Top >= current.Bottom);
                        break;
                    case NavigationDirection.Right:
                        isAdjacent = candidate.Left - current.Right is 0 or 1;
                        hasOverlap = !(candidate.Bottom <= current.Top || candidate.Top >= current.Bottom);
                        break;
                    case NavigationDirection.Up:
                        isAdjacent = current.Top - candidate.Bottom is 0 or 1;
                        hasOverlap = !(candidate.Right <= current.Left || candidate.Left >= current.Right);
                        break;
                    case NavigationDirection.Down:
                        isAdjacent = candidate.Top - current.Bottom is 0 or 1;
                        hasOverlap = !(candidate.Right <= current.Left || candidate.Left >= current.Right);
                        break;
                    default:
                        continue;
                }

                if (isAdjacent && hasOverlap)
                    result.Add(candidate);
            }

            // Sort by position to match visual layout
            return direction switch
            {
                NavigationDirection.Up or NavigationDirection.Down => result.OrderBy(d => d.Left).ToImmutableList(),
                NavigationDirection.Left or NavigationDirection.Right => result.OrderBy(d => d.Top).ToImmutableList(),
                _ => result.ToImmutable()
            };
        }
    }

    public async Task SelectDisplayAsync(string displayId)
    {
        try
        {
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, displayId);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Display.Select, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to select display {DisplayId}", displayId);
        }
    }

    public async Task SendMouseMoveAsync(float x, float y)
    {
        try
        {
            var message = new MouseMoveMessage(x, y);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.MouseMove, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse move");
        }
    }

    public async Task SendMouseDownAsync(MouseButton button, float x, float y)
    {
        try
        {
            var message = new MouseButtonMessage(button, x, y);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.MouseDown, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse down");
        }
    }

    public async Task SendMouseUpAsync(MouseButton button, float x, float y)
    {
        try
        {
            var message = new MouseButtonMessage(button, x, y);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.MouseUp, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse up");
        }
    }

    public async Task SendMouseWheelAsync(float deltaX, float deltaY, float x, float y)
    {
        try
        {
            var message = new MouseWheelMessage(deltaX, deltaY, x, y);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.MouseWheel, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse wheel");
        }
    }

    public async Task SendKeyDownAsync(ushort keyCode, KeyModifiers modifiers)
    {
        try
        {
            this._pressedKeys.TryAdd(keyCode, null);

            var message = new KeyMessage(keyCode, modifiers);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.KeyDown, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key down");
        }
    }

    public async Task SendKeyUpAsync(ushort keyCode, KeyModifiers modifiers)
    {
        try
        {
            this._pressedKeys.TryRemove(keyCode, out _);

            var message = new KeyMessage(keyCode, modifiers);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.KeyUp, buffer.WrittenMemory);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key up");
        }
    }

    public async Task ReleaseAllKeysAsync()
    {
        if (this._pressedKeys.IsEmpty)
            return;

        foreach (var keyCode in this._pressedKeys.Keys)
        {
            try
            {
                this._pressedKeys.TryRemove(keyCode, out _);

                var message = new KeyMessage(keyCode, KeyModifiers.None);
                using var buffer = PooledBufferWriter.Rent();
                ProtocolSerializer.Serialize(buffer, message);
                await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.KeyUp, buffer.WrittenMemory);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to release key");
            }
        }
    }

    public async Task SendCtrlAltDelAsync()
    {
        try
        {
            await ((IConnectionImpl)this._connection).SendInputAsync(MessageTypes.Input.SecureAttentionSequence, ReadOnlyMemory<byte>.Empty);
            this._logger.LogInformation("Sent Ctrl+Alt+Del request");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send Ctrl+Alt+Del");
        }
    }

    void IViewerServiceImpl.HandleFrame(string displayId, ulong frameNumber, FrameCodec codec, FrameRegion[] regions)
    {
        // Track current display from incoming frames
        bool displayChanged;
        using (this._displayLock.EnterScope())
        {
            displayChanged = this._currentDisplayId != displayId;
            if (displayChanged)
            {
                this._currentDisplayId = displayId;
            }
        }

        if (displayChanged)
        {
            this.CurrentDisplayChanged?.Invoke(this, EventArgs.Empty);
        }

        var args = new FrameReceivedEventArgs(displayId, frameNumber, codec, regions);
        this.FrameReady?.Invoke(this, args);
    }

    private async void WindowsKeyBlocker_ShortcutIntercepted(InterceptedShortcut shortcut)
    {
        if (!this.IsInputEnabled)
            return;

        var modifiers = KeyModifiers.None;
        if (shortcut.Alt) modifiers |= KeyModifiers.Alt;
        if (shortcut.Ctrl) modifiers |= KeyModifiers.Control;
        if (shortcut.Shift) modifiers |= KeyModifiers.Shift;

        if (shortcut.IsKeyDown)
            await this.SendKeyDownAsync(shortcut.VirtualKeyCode, modifiers);
        else
            await this.SendKeyUpAsync(shortcut.VirtualKeyCode, modifiers);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._windowsKeyBlockerService.ShortcutIntercepted -= this.WindowsKeyBlocker_ShortcutIntercepted;
        this._connection.ConnectionPropertiesChanged -= this.Connection_ConnectionPropertiesChanged;
    }
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Viewer;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class ViewerConnectionService : IViewerServiceImpl, IDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<ViewerConnectionService> _logger;
    private readonly IWindowsKeyBlockerService _windowsKeyBlockerService;
    private readonly ConcurrentDictionary<ushort, object?> _pressedKeys = new();
    private bool _disposed;

    public ViewerConnectionService(
        Connection connection,
        IWindowsKeyBlockerService windowsKeyBlockerService,
        ILogger<ViewerConnectionService> logger)
    {
        this._connection = connection;
        this._windowsKeyBlockerService = windowsKeyBlockerService;
        this._logger = logger;

        this._windowsKeyBlockerService.WindowsKeyDown += this.WindowsKeyBlocker_WindowsKeyDown;
        this._windowsKeyBlockerService.WindowsKeyUp += this.WindowsKeyBlocker_WindowsKeyUp;
    }

    public bool IsInputEnabled { get; set; } = true;

    public event EventHandler<FrameReceivedEventArgs>? FrameReady;

    public async Task SendMouseMoveAsync(float x, float y)
    {
        try
        {
            var message = new MouseMoveMessage(x, y);
            using var buffer = PooledBufferWriter.Rent();
            ProtocolSerializer.Serialize(buffer, message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseMove, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.MouseDown, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.MouseUp, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.MouseWheel, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.KeyDown, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, buffer.WrittenMemory);
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
                await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, buffer.WrittenMemory);
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
            await this._connection.SendInputAsync(MessageTypes.Input.SecureAttentionSequence, ReadOnlyMemory<byte>.Empty);
            this._logger.LogInformation("Sent Ctrl+Alt+Del request");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send Ctrl+Alt+Del");
        }
    }

    public async Task SwitchDisplayAsync()
    {
        await this._connection.SwitchDisplayAsync();
    }

    private const ushort WindowsKeyCode = 0x5B;

    public Task SendWindowsKeyDownAsync()
    {
        return this.SendKeyDownAsync(WindowsKeyCode, KeyModifiers.None);
    }

    public Task SendWindowsKeyUpAsync()
    {
        return this.SendKeyUpAsync(WindowsKeyCode, KeyModifiers.None);
    }

    void IViewerServiceImpl.HandleFrame(string displayId, ulong frameNumber, FrameCodec codec, FrameRegion[] regions)
    {
        var args = new FrameReceivedEventArgs(displayId, frameNumber, codec, regions);
        this.FrameReady?.Invoke(this, args);
    }

    private async void WindowsKeyBlocker_WindowsKeyDown(ushort vkCode)
    {
        if (!this.IsInputEnabled)
            return;

        await this.SendWindowsKeyDownAsync();
    }

    private async void WindowsKeyBlocker_WindowsKeyUp(ushort vkCode)
    {
        if (!this.IsInputEnabled)
            return;

        await this.SendWindowsKeyUpAsync();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._windowsKeyBlockerService.WindowsKeyDown -= this.WindowsKeyBlocker_WindowsKeyDown;
        this._windowsKeyBlockerService.WindowsKeyUp -= this.WindowsKeyBlocker_WindowsKeyUp;
    }
}

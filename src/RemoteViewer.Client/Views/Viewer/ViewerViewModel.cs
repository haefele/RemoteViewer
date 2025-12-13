using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Server.SharedAPI.Protocol;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<ViewerViewModel> _logger;
    private readonly FrameCompositor _compositor = new();

    public ToastsViewModel Toasts { get; }

    private readonly ConcurrentDictionary<ushort, object?> _pressedKeys = new();

    private bool _disposed;
    private ulong _lastReceivedFrameNumber;

    [ObservableProperty]
    private WriteableBitmap? _frameBitmap;

    [ObservableProperty]
    private WriteableBitmap? _debugOverlayBitmap;

    [ObservableProperty]
    private string _title = "Remote Viewer";

    [ObservableProperty]
    private string? _selectedDisplayId;

    async partial void OnSelectedDisplayIdChanged(string? value)
    {
        this._logger.LogInformation("Selected display {DisplayId}", value);

        var display = this.Displays.FirstOrDefault(d => d.Id == value);
        this.Title = display is null ? "Remote Viewer" : $"Remote Viewer - {display.Name}";

        // Send display selection to presenter
        if (display is not null)
        {
            await this._connection.SelectDisplayAsync(display.Id);
        }
    }

    [ObservableProperty]
    private ObservableCollection<DisplayInfo> _displays = new();

    [ObservableProperty]
    private bool _isConnected = true;

    [ObservableProperty]
    private string _statusText = "Waiting for display list...";

    public event EventHandler? CloseRequested;

    public ViewerViewModel(Connection connection, IViewModelFactory viewModelFactory, ILogger<ViewerViewModel> logger)
    {
        this._connection = connection;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        // Subscribe to Connection events
        this._connection.DisplaysChanged += this.Connection_DisplaysChanged;
        this._connection.FrameReceived += this.Connection_FrameReceived;
        this._connection.Closed += this.Connection_Closed;

        this.Title = $"Remote Viewer - {connection.ConnectionId[..8]}...";
    }

    private void Connection_DisplaysChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var displays = this._connection.Displays;

            this.Displays.Clear();
            foreach (var display in displays)
            {
                this.Displays.Add(display);
            }

            this.SelectedDisplayId ??= displays.FirstOrDefault(d => d.IsPrimary)?.Id;
            this.StatusText = $"{this.Displays.Count} display(s) available";
        });
    }

    private void Connection_FrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        // Only process frames for the selected display
        if (e.DisplayId != this.SelectedDisplayId)
            return;

        // Drop out-of-order delta frames (but always accept keyframes)
        if (e.Regions is not [{ IsKeyframe: true }] && e.FrameNumber <= this._lastReceivedFrameNumber)
            return;

        try
        {
            if (e.Regions is [{ IsKeyframe: true }])
            {
                // Apply full keyframe
                this._compositor.ApplyKeyframe(e.Regions, e.FrameNumber);
            }
            else
            {
                // Apply delta regions
                this._compositor.ApplyDeltaRegions(e.Regions, e.FrameNumber);
            }

            this._lastReceivedFrameNumber = e.FrameNumber;

            Dispatcher.UIThread.Post(() =>
            {
                // Check if disposed before updating bitmap
                if (this._disposed)
                    return;

                // Force UI update by reassigning the bitmap reference if it changed
                if (this._compositor.Canvas is { } canvas && this.FrameBitmap != canvas)
                {
                    this.FrameBitmap = canvas;
                }
                else
                {
                    // Force property change notification for in-place updates
                    this.OnPropertyChanged(nameof(this.FrameBitmap));
                }

                // Update debug overlay
                if (this._compositor.DebugOverlay is { } overlay && this.DebugOverlayBitmap != overlay)
                {
                    this.DebugOverlayBitmap = overlay;
                }
                else if (this._compositor.DebugOverlay is not null)
                {
                    this.OnPropertyChanged(nameof(this.DebugOverlayBitmap));
                }
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error processing frame");
        }
    }

    private void Connection_Closed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }

    public async Task SendMouseMoveAsync(float x, float y)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            var message = new MouseMoveMessage(x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseMove, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse move");
        }
    }

    public async Task SendMouseDownAsync(MouseButton button, float x, float y)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            var message = new MouseButtonMessage(button, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseDown, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse down");
        }
    }

    public async Task SendMouseUpAsync(MouseButton button, float x, float y)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            var message = new MouseButtonMessage(button, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseUp, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse up");
        }
    }

    public async Task SendMouseWheelAsync(float deltaX, float deltaY, float x, float y)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            var message = new MouseWheelMessage(deltaX, deltaY, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseWheel, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse wheel");
        }
    }

    public async Task SendKeyDownAsync(ushort keyCode, KeyModifiers modifiers)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            this._pressedKeys.TryAdd(keyCode, null);

            var message = new KeyMessage(keyCode, modifiers);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.KeyDown, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key down");
        }
    }

    public async Task SendKeyUpAsync(ushort keyCode, KeyModifiers modifiers)
    {
        if (this.IsConnected is false || this.SelectedDisplayId is null)
            return;

        try
        {
            this._pressedKeys.TryRemove(keyCode, out _);

            var message = new KeyMessage(keyCode, modifiers);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key up");
        }
    }

    public async Task ReleaseAllKeysAsync()
    {
        if (this.IsConnected is false || this._pressedKeys.IsEmpty || this.SelectedDisplayId is null)
            return;

        foreach (var keyCode in this._pressedKeys.Keys)
        {
            try
            {
                this._pressedKeys.TryRemove(keyCode, out _);

                var message = new KeyMessage(keyCode, KeyModifiers.None);
                var data = ProtocolSerializer.Serialize(message);
                await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, data);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to release key");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        await this._connection.DisconnectAsync();

        // Unsubscribe from Connection events
        this._connection.DisplaysChanged -= this.Connection_DisplaysChanged;
        this._connection.FrameReceived -= this.Connection_FrameReceived;
        this._connection.Closed -= this.Connection_Closed;

        this._compositor.Dispose();

        GC.SuppressFinalize(this);
    }
}

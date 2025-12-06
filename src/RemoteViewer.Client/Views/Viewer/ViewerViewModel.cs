using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Server.SharedAPI.Protocol;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Views.Viewer;

/// <summary>
/// ViewModel for the viewer window that displays remote screen and handles input capture.
/// </summary>
public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<ViewerViewModel> _logger;
    private readonly FrameCompositor _compositor = new();

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

    public ViewerViewModel(
        Connection connection,
        ILogger<ViewerViewModel> logger)
    {
        this._connection = connection;
        this._logger = logger;

        // Subscribe to Connection events
        this._connection.DisplaysChanged += this.OnDisplaysChanged;
        this._connection.FrameReceived += this.OnFrameReceived;
        this._connection.Closed += this.OnConnectionClosed;

        this.Title = $"Remote Viewer - {connection.ConnectionId[..8]}...";
    }

    private void OnDisplaysChanged(object? sender, EventArgs e)
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

    private void OnFrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        // Only process frames for the selected display
        if (e.DisplayId != this.SelectedDisplayId)
            return;

        // Drop out-of-order delta frames (but always accept keyframes)
        if (e.FrameType != FrameType.Keyframe && e.FrameNumber <= this._lastReceivedFrameNumber)
            return;

        try
        {
            if (e.FrameType == FrameType.Keyframe)
            {
                // Apply full keyframe
                this._compositor.ApplyKeyframe(e.Regions, e.Width, e.Height, e.FrameNumber);
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

    private void OnConnectionClosed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.IsConnected = false;
            this.StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }

    public void SendMouseMove(float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseMoveMessage(x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.MouseMove, data);
    }

    public void SendMouseDown(MouseButton button, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.MouseDown, data);
    }

    public void SendMouseUp(MouseButton button, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.MouseUp, data);
    }

    public void SendMouseWheel(float deltaX, float deltaY, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseWheelMessage(deltaX, deltaY, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.MouseWheel, data);
    }

    public void SendKeyDown(ushort keyCode, KeyModifiers modifiers)
    {
        if (!this.IsConnected)
            return;

        var message = new KeyMessage(keyCode, modifiers);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.KeyDown, data);
    }

    public void SendKeyUp(ushort keyCode, KeyModifiers modifiers)
    {
        if (!this.IsConnected)
            return;

        var message = new KeyMessage(keyCode, modifiers);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._connection.SendInputAsync(MessageTypes.Input.KeyUp, data);
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        await this.DisconnectCommand.ExecuteAsync(null);

        this._disposed = true;

        // Unsubscribe from Connection events
        this._connection.DisplaysChanged -= this.OnDisplaysChanged;
        this._connection.FrameReceived -= this.OnFrameReceived;
        this._connection.Closed -= this.OnConnectionClosed;

        this._compositor.Dispose();
        this.FrameBitmap = null;

        GC.SuppressFinalize(this);
    }
}

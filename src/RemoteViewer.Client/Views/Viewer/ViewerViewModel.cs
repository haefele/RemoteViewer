using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Views.Viewer;

/// <summary>
/// ViewModel for the viewer window that displays remote screen and handles input capture.
/// </summary>
public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<ViewerViewModel> _logger;
    private readonly string _connectionId;

    private bool _disposed;

    [ObservableProperty]
    private Bitmap? _frameBitmap;

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
            var message = new DisplaySelectMessage(display.Id);
            var messageData = ProtocolSerializer.Serialize(message);
            await this._hubClient.SendMessage(this._connectionId, MessageTypes.Display.Select, messageData, MessageDestination.PresenterOnly);
        }
    }

    [ObservableProperty]
    private ObservableCollection<DisplayInfo> _displays = new();

    [ObservableProperty]
    private bool _isConnected = true;

    [ObservableProperty]
    private string _statusText = "Waiting for display list...";

    [ObservableProperty]
    private int _frameWidth;

    [ObservableProperty]
    private int _frameHeight;

    public event EventHandler? CloseRequested;

    public ViewerViewModel(
        ConnectionHubClient hubClient,
        string connectionId,
        ILogger<ViewerViewModel> logger)
    {
        this._hubClient = hubClient;
        this._connectionId = connectionId;
        this._logger = logger;

        this._hubClient.MessageReceived += this.OnMessageReceived;
        this._hubClient.ConnectionStopped += this.OnConnectionStopped;

        this.Title = $"Remote Viewer - {connectionId}";

        // Request display list from presenter
        _ = this.RequestDisplayListAsync();
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.ConnectionId != this._connectionId)
            return;

        try
        {
            switch (e.MessageType)
            {
                case MessageTypes.Display.List:
                    this.HandleDisplayList(e.Data);
                    break;

                case MessageTypes.Screen.Frame:
                    this.HandleFrame(e.Data);
                    break;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling message {MessageType}", e.MessageType);
        }
    }

    private void HandleDisplayList(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<DisplayListMessage>(data);

        Dispatcher.UIThread.Post(() =>
        {
            this.Displays.Clear();
            foreach (var display in message.Displays)
            {
                this.Displays.Add(display);
            }

            this.SelectedDisplayId ??= message.Displays.FirstOrDefault(d => d.IsPrimary)?.Id;
            this.StatusText = $"{this.Displays.Count} display(s) available";
        });
    }

    private void HandleFrame(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<FrameMessage>(data);

        // Only process frames for the selected display
        if (message.DisplayId != this.SelectedDisplayId)
            return;

        try
        {
            using var stream = new MemoryStream(message.Data.ToArray());
            var bitmap = new Bitmap(stream);

            Dispatcher.UIThread.Post(() =>
            {
                // Check if disposed before updating bitmap
                if (this._disposed)
                {
                    bitmap.Dispose();
                    return;
                }

                var oldBitmap = this.FrameBitmap;
                this.FrameBitmap = bitmap;
                this.FrameWidth = message.Width;
                this.FrameHeight = message.Height;
                oldBitmap?.Dispose();
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error decoding frame");
        }
    }

    private void OnConnectionStopped(object? sender, ConnectionStoppedEventArgs e)
    {
        if (e.ConnectionId != this._connectionId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            this.IsConnected = false;
            this.StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private async Task RequestDisplayListAsync()
    {
        var message = new RequestDisplayListMessage();
        var data = ProtocolSerializer.Serialize(message);
        await this._hubClient.SendMessage(this._connectionId, MessageTypes.Display.RequestList, data, MessageDestination.PresenterOnly);
        this._logger.LogInformation("Requested display list from presenter");
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this._connectionId);
        await this._hubClient.Disconnect(this._connectionId);
    }

    public void SendMouseMove(float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseMoveMessage(x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.MouseMove, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseDown(MouseButton button, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.MouseDown, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseUp(MouseButton button, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.MouseUp, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseWheel(float deltaX, float deltaY, float x, float y)
    {
        if (!this.IsConnected || this.SelectedDisplayId is null)
            return;

        var message = new MouseWheelMessage(deltaX, deltaY, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.MouseWheel, data, MessageDestination.PresenterOnly);
    }

    public void SendKeyDown(ushort keyCode, ushort scanCode, KeyModifiers modifiers, bool isExtendedKey)
    {
        if (!this.IsConnected)
            return;

        var message = new KeyMessage(keyCode, scanCode, modifiers, isExtendedKey);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.KeyDown, data, MessageDestination.PresenterOnly);
    }

    public void SendKeyUp(ushort keyCode, ushort scanCode, KeyModifiers modifiers, bool isExtendedKey)
    {
        if (!this.IsConnected)
            return;

        var message = new KeyMessage(keyCode, scanCode, modifiers, isExtendedKey);
        var data = ProtocolSerializer.Serialize(message);
        _ = this._hubClient.SendMessage(this._connectionId, MessageTypes.Input.KeyUp, data, MessageDestination.PresenterOnly);
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        await this.DisconnectCommand.ExecuteAsync(null);

        this._disposed = true;

        this._hubClient.MessageReceived -= this.OnMessageReceived;
        this._hubClient.ConnectionStopped -= this.OnConnectionStopped;

        this.FrameBitmap?.Dispose();
        this.FrameBitmap = null;

        GC.SuppressFinalize(this);
    }
}

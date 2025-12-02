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
public partial class ViewerViewModel : ViewModelBase, IDisposable
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
        _hubClient = hubClient;
        _connectionId = connectionId;
        _logger = logger;

        _hubClient.MessageReceived += OnMessageReceived;
        _hubClient.ConnectionStopped += OnConnectionStopped;

        Title = $"Remote Viewer - {connectionId}";
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.ConnectionId != _connectionId)
            return;

        try
        {
            switch (e.MessageType)
            {
                case MessageTypes.Display.List:
                    HandleDisplayList(e.Data);
                    break;

                case MessageTypes.Screen.Frame:
                    HandleFrame(e.Data);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message {MessageType}", e.MessageType);
        }
    }

    private void HandleDisplayList(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<DisplayListMessage>(data);

        Dispatcher.UIThread.Post(() =>
        {
            Displays.Clear();
            foreach (var display in message.Displays)
            {
                Displays.Add(display);
            }

            // Auto-select primary display if no display is selected
            if (SelectedDisplayId is null)
            {
                var primary = message.Displays.FirstOrDefault(d => d.IsPrimary)
                    ?? message.Displays.FirstOrDefault();

                if (primary is not null)
                {
                    SelectDisplay(primary.Id);
                }
            }

            StatusText = $"{Displays.Count} display(s) available";
        });
    }

    private void HandleFrame(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<FrameMessage>(data);

        // Only process frames for the selected display
        if (message.DisplayId != SelectedDisplayId)
            return;

        try
        {
            using var stream = new MemoryStream(message.Data.ToArray());
            var bitmap = new Bitmap(stream);

            Dispatcher.UIThread.Post(() =>
            {
                var oldBitmap = FrameBitmap;
                FrameBitmap = bitmap;
                FrameWidth = message.Width;
                FrameHeight = message.Height;
                oldBitmap?.Dispose();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decoding frame");
        }
    }

    private void OnConnectionStopped(object? sender, ConnectionStoppedEventArgs e)
    {
        if (e.ConnectionId != _connectionId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = false;
            StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        _logger.LogInformation("User requested to disconnect from connection {ConnectionId}", _connectionId);
        await _hubClient.Disconnect(_connectionId);
        // The ConnectionStopped event will trigger CloseRequested
    }

    [RelayCommand]
    private void SelectDisplay(string displayId)
    {
        if (SelectedDisplayId == displayId)
            return;

        SelectedDisplayId = displayId;
        _logger.LogInformation("Selected display {DisplayId}", displayId);

        var display = Displays.FirstOrDefault(d => d.Id == displayId);
        if (display is not null)
        {
            Title = $"Remote Viewer - {display.Name}";
        }

        // Send display selection to presenter
        var message = new DisplaySelectMessage(displayId);
        var messageData = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Display.Select, messageData, MessageDestination.PresenterOnly);
    }

    public void SendMouseMove(float x, float y)
    {
        if (!IsConnected || SelectedDisplayId is null)
            return;

        var message = new MouseMoveMessage(x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.MouseMove, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseDown(MouseButton button, float x, float y)
    {
        if (!IsConnected || SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.MouseDown, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseUp(MouseButton button, float x, float y)
    {
        if (!IsConnected || SelectedDisplayId is null)
            return;

        var message = new MouseButtonMessage(button, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.MouseUp, data, MessageDestination.PresenterOnly);
    }

    public void SendMouseWheel(float deltaX, float deltaY, float x, float y)
    {
        if (!IsConnected || SelectedDisplayId is null)
            return;

        var message = new MouseWheelMessage(deltaX, deltaY, x, y);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.MouseWheel, data, MessageDestination.PresenterOnly);
    }

    public void SendKeyDown(ushort keyCode, ushort scanCode, KeyModifiers modifiers, bool isExtendedKey)
    {
        if (!IsConnected)
            return;

        var message = new KeyMessage(keyCode, scanCode, modifiers, isExtendedKey);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.KeyDown, data, MessageDestination.PresenterOnly);
    }

    public void SendKeyUp(ushort keyCode, ushort scanCode, KeyModifiers modifiers, bool isExtendedKey)
    {
        if (!IsConnected)
            return;

        var message = new KeyMessage(keyCode, scanCode, modifiers, isExtendedKey);
        var data = ProtocolSerializer.Serialize(message);
        _ = _hubClient.SendMessage(_connectionId, MessageTypes.Input.KeyUp, data, MessageDestination.PresenterOnly);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _hubClient.MessageReceived -= OnMessageReceived;
        _hubClient.ConnectionStopped -= OnConnectionStopped;

        FrameBitmap?.Dispose();
        FrameBitmap = null;

        GC.SuppressFinalize(this);
    }
}

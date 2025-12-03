using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;
using SkiaSharp;
using System.Collections.Concurrent;

namespace RemoteViewer.Client.Views.Presenter;

/// <summary>
/// ViewModel for the presenter window that handles presentation status, screen capture,
/// frame sending, and input injection.
/// </summary>
public partial class PresenterViewModel : ViewModelBase, IDisposable
{
    private readonly ConnectionHubClient _hubClient;
    private readonly IScreenshotService _screenshotService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILogger<PresenterViewModel> _logger;
    private readonly string _connectionId;

    // Thread-safe viewer tracking
    private readonly ConcurrentDictionary<string, byte> _knownViewers = new();
    private readonly ConcurrentDictionary<string, string> _viewerDisplaySubscriptions = new(); // viewerClientId -> displayId

    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "Presenting";

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private bool _isPresenting = true;

    [ObservableProperty]
    private string _statusText = "Waiting for viewers...";

    [ObservableProperty]
    private bool _isPlatformSupported;

    public event EventHandler? CloseRequested;

    public PresenterViewModel(
        ConnectionHubClient hubClient,
        string connectionId,
        IScreenshotService screenshotService,
        IInputInjectionService inputInjectionService,
        ILogger<PresenterViewModel> logger)
    {
        _hubClient = hubClient;
        _connectionId = connectionId;
        _screenshotService = screenshotService;
        _inputInjectionService = inputInjectionService;
        _logger = logger;

        IsPlatformSupported = screenshotService.IsSupported;

        if (!IsPlatformSupported)
        {
            Title = "Presenter - Not Supported";
            StatusText = "Not supported on this platform";
            IsPresenting = false;
            return;
        }

        Title = $"Presenting - {connectionId[..8]}...";

        // Subscribe to hub events
        _hubClient.ConnectionChanged += OnConnectionChanged;
        _hubClient.ConnectionStopped += OnConnectionStopped;
        _hubClient.MessageReceived += OnMessageReceived;
        _hubClient.Reconnecting += OnHubReconnecting;

        // Start presenting immediately
        StartPresenting();
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (e.ConnectionInfo.ConnectionId != _connectionId)
            return;

        // Only handle if we're the presenter
        if (_hubClient.ClientId != e.ConnectionInfo.PresenterClientId)
            return;

        // Update viewer list and send display list to new viewers
        var currentViewerKeys = _knownViewers.Keys;
        var newViewers = e.ConnectionInfo.ViewerClientIds.ToHashSet();

        // Find new viewers
        var addedViewers = newViewers.Except(currentViewerKeys).ToList();
        if (addedViewers.Count > 0)
        {
            _logger.LogInformation("New viewers joined: {Viewers}", string.Join(", ", addedViewers));
            foreach (var viewerId in addedViewers)
            {
                _knownViewers.TryAdd(viewerId, 0);
            }
        }

        // Remove disconnected viewers
        var removedViewers = currentViewerKeys.Except(newViewers).ToList();
        foreach (var viewerId in removedViewers)
        {
            _knownViewers.TryRemove(viewerId, out _);
            _viewerDisplaySubscriptions.TryRemove(viewerId, out _);
        }

        // Update UI
        Dispatcher.UIThread.Post(() =>
        {
            ViewerCount = e.ConnectionInfo.ViewerClientIds.Count;
            StatusText = ViewerCount == 0
                ? "Waiting for viewers..."
                : $"{ViewerCount} viewer(s) connected";
        });
    }

    private void OnConnectionStopped(object? sender, ConnectionStoppedEventArgs e)
    {
        if (e.ConnectionId != _connectionId)
            return;

        StopPresenting();

        Dispatcher.UIThread.Post(() =>
        {
            IsPresenting = false;
            StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnHubReconnecting(object? sender, ReconnectingEventArgs e)
    {
        StopPresenting();

        Dispatcher.UIThread.Post(() =>
        {
            IsPresenting = false;
            StatusText = "Hub connection lost";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (e.ConnectionId != _connectionId)
            return;

        try
        {
            HandleMessage(e.SenderClientId, e.MessageType, e.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message {MessageType} from {SenderClientId}", e.MessageType, e.SenderClientId);
        }
    }

    private void HandleMessage(string senderClientId, string messageType, ReadOnlyMemory<byte> data)
    {
        switch (messageType)
        {
            case MessageTypes.Display.Select:
                HandleDisplaySelect(senderClientId, data);
                break;

            case MessageTypes.Display.RequestList:
                HandleDisplayListRequest(senderClientId);
                break;

            case MessageTypes.Input.MouseMove:
                HandleMouseMove(senderClientId, data);
                break;

            case MessageTypes.Input.MouseDown:
                HandleMouseButton(senderClientId, data, isDown: true);
                break;

            case MessageTypes.Input.MouseUp:
                HandleMouseButton(senderClientId, data, isDown: false);
                break;

            case MessageTypes.Input.MouseWheel:
                HandleMouseWheel(senderClientId, data);
                break;

            case MessageTypes.Input.KeyDown:
                HandleKey(data, isDown: true);
                break;

            case MessageTypes.Input.KeyUp:
                HandleKey(data, isDown: false);
                break;
        }
    }

    private void HandleDisplaySelect(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<DisplaySelectMessage>(data);
        _viewerDisplaySubscriptions[senderClientId] = message.DisplayId;
        _logger.LogInformation("Viewer {ViewerId} selected display {DisplayId}", senderClientId, message.DisplayId);
    }

    private async void HandleDisplayListRequest(string senderClientId)
    {
        var displays = _screenshotService.GetDisplays();
        var displayInfos = displays.Select(d => new DisplayInfo(
            d.Name,
            d.Name,
            d.IsPrimary,
            d.Bounds.Left,
            d.Bounds.Top,
            d.Bounds.Width,
            d.Bounds.Height
        )).ToArray();

        var message = new DisplayListMessage(displayInfos);
        var data = ProtocolSerializer.Serialize(message);

        await _hubClient.SendMessage(_connectionId, MessageTypes.Display.List, data, MessageDestination.SpecificClients, [senderClientId]);
        _logger.LogInformation("Sent display list to viewer {ViewerId}", senderClientId);
    }

    private void HandleMouseMove(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<MouseMoveMessage>(data);

        if (_viewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
        {
            var display = GetDisplayById(displayId);
            if (display is not null)
            {
                _inputInjectionService.InjectMouseMove(display, message.X, message.Y);
            }
        }
    }

    private void HandleMouseButton(string senderClientId, ReadOnlyMemory<byte> data, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<MouseButtonMessage>(data);

        if (_viewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
        {
            var display = GetDisplayById(displayId);
            if (display is not null)
            {
                _inputInjectionService.InjectMouseButton(display, message.Button, isDown, message.X, message.Y);
            }
        }
    }

    private void HandleMouseWheel(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<MouseWheelMessage>(data);

        if (_viewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
        {
            var display = GetDisplayById(displayId);
            if (display is not null)
            {
                _inputInjectionService.InjectMouseWheel(display, message.DeltaX, message.DeltaY, message.X, message.Y);
            }
        }
    }

    private void HandleKey(ReadOnlyMemory<byte> data, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<KeyMessage>(data);
        _inputInjectionService.InjectKey(message.KeyCode, message.ScanCode, isDown, message.IsExtendedKey);
    }

    private Display? GetDisplayById(string displayId)
    {
        return _screenshotService.GetDisplays().FirstOrDefault(d => d.Name == displayId);
    }

    private void StartPresenting()
    {
        _logger.LogInformation("Starting presenter for connection {ConnectionId}", _connectionId);

        _captureLoopCts = new CancellationTokenSource();
        _captureLoopTask = Task.Run(() => CaptureLoopAsync(_captureLoopCts.Token));
    }

    private void StopPresenting()
    {
        _captureLoopCts?.Cancel();
        _logger.LogInformation("Stopped presenting for connection {ConnectionId}", _connectionId);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        const int TargetFps = 30;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
        ulong frameNumber = 0;

        _logger.LogInformation("Capture loop started for connection {ConnectionId}", _connectionId);

        while (!ct.IsCancellationRequested)
        {
            var frameStart = DateTime.UtcNow;

            try
            {
                await CaptureAndSendFramesAsync(frameNumber++);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in capture loop");
            }

            // Maintain target FPS
            var elapsed = DateTime.UtcNow - frameStart;
            var delay = frameInterval - elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }
        }

        _logger.LogInformation("Capture loop ended for connection {ConnectionId}", _connectionId);
    }

    private async Task CaptureAndSendFramesAsync(ulong frameNumber)
    {
        // Group viewers by display
        var viewersByDisplay = _viewerDisplaySubscriptions
            .GroupBy(kvp => kvp.Value)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Key).ToList());

        if (viewersByDisplay.Count == 0)
            return;

        var displays = _screenshotService.GetDisplays();

        foreach (var (displayId, viewers) in viewersByDisplay)
        {
            var display = displays.FirstOrDefault(d => d.Name == displayId);
            if (display is null)
                continue;

            var captureResult = _screenshotService.CaptureDisplay(display);
            if (!captureResult.Success || captureResult.Bitmap is null)
                continue;

            try
            {
                // Encode to JPEG
                var frameData = EncodeJpeg(captureResult.Bitmap, quality: 75);

                var message = new FrameMessage(
                    displayId,
                    frameNumber,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    FrameCodec.Jpeg,
                    captureResult.Bitmap.Width,
                    captureResult.Bitmap.Height,
                    75,
                    frameData
                );

                var data = ProtocolSerializer.Serialize(message);

                // Send to specific viewers watching this display
                await _hubClient.SendMessage(_connectionId, MessageTypes.Screen.Frame, data, MessageDestination.SpecificClients, viewers);
            }
            finally
            {
                captureResult.Bitmap.Dispose();
            }
        }
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    [RelayCommand]
    private async Task StopPresentingAsync()
    {
        if (!IsPlatformSupported)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _logger.LogInformation("User requested to stop presenting for connection {ConnectionId}", _connectionId);
        await _hubClient.Disconnect(_connectionId);
        // The ConnectionStopped event will trigger CloseRequested
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop capture loop
        StopPresenting();
        _captureLoopCts?.Dispose();

        // Unsubscribe from hub events
        _hubClient.ConnectionChanged -= OnConnectionChanged;
        _hubClient.ConnectionStopped -= OnConnectionStopped;
        _hubClient.MessageReceived -= OnMessageReceived;
        _hubClient.Reconnecting -= OnHubReconnecting;

        GC.SuppressFinalize(this);
    }
}

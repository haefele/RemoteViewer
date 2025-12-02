#if WINDOWS
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;
using RemoteViewer.WinServ.Services;
using SkiaSharp;
using System.Collections.Concurrent;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Handles presenter responsibilities: screen capture, frame sending, and input injection.
/// </summary>
public class PresenterService : IDisposable
{
    private readonly ConnectionHubClient _hubClient;
    private readonly IScreenshotService _screenshotService;
    private readonly InputInjectionService _inputInjectionService;
    private readonly ILogger<PresenterService> _logger;

    private readonly ConcurrentDictionary<string, ConnectionPresenterState> _connections = new();
    private bool _disposed;

    public PresenterService(
        ConnectionHubClient hubClient,
        IScreenshotService screenshotService,
        InputInjectionService inputInjectionService,
        ILogger<PresenterService> logger)
    {
        _hubClient = hubClient;
        _screenshotService = screenshotService;
        _inputInjectionService = inputInjectionService;
        _logger = logger;

        // Subscribe to hub events
        _hubClient.ConnectionStarted += OnConnectionStarted;
        _hubClient.ConnectionChanged += OnConnectionChanged;
        _hubClient.ConnectionStopped += OnConnectionStopped;
        _hubClient.MessageReceived += OnMessageReceived;
    }

    private void OnConnectionStarted(object? sender, ConnectionStartedEventArgs e)
    {
        if (e.IsPresenter)
        {
            _logger.LogInformation("Starting presenter for connection {ConnectionId}", e.ConnectionId);
            StartPresenting(e.ConnectionId);
        }
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        var connectionId = e.ConnectionInfo.ConnectionId;

        // Only handle if we're the presenter
        if (_hubClient.ClientId != e.ConnectionInfo.PresenterClientId)
            return;

        if (_connections.TryGetValue(connectionId, out var state))
        {
            // Update viewer list and send display list to new viewers
            var currentViewers = state.KnownViewers;
            var newViewers = e.ConnectionInfo.ViewerClientIds.ToHashSet();

            // Find new viewers
            var addedViewers = newViewers.Except(currentViewers).ToList();
            if (addedViewers.Count > 0)
            {
                _logger.LogInformation("New viewers joined: {Viewers}", string.Join(", ", addedViewers));
                foreach (var viewerId in addedViewers)
                {
                    state.KnownViewers.Add(viewerId);
                }
                _ = SendDisplayListAsync(connectionId);
            }

            // Remove disconnected viewers
            var removedViewers = currentViewers.Except(newViewers).ToList();
            foreach (var viewerId in removedViewers)
            {
                state.KnownViewers.Remove(viewerId);
                state.ViewerDisplaySubscriptions.TryRemove(viewerId, out _);
            }
        }
    }

    private void OnConnectionStopped(object? sender, ConnectionStoppedEventArgs e)
    {
        StopPresenting(e.ConnectionId);
    }

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Only handle if we're the presenter for this connection
        if (!_connections.TryGetValue(e.ConnectionId, out var state))
            return;

        try
        {
            HandleMessage(e.ConnectionId, e.SenderClientId, e.MessageType, e.Data, state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling message {MessageType} from {SenderClientId}", e.MessageType, e.SenderClientId);
        }
    }

    private void HandleMessage(string connectionId, string senderClientId, string messageType, ReadOnlyMemory<byte> data, ConnectionPresenterState state)
    {
        switch (messageType)
        {
            case MessageTypes.Display.Select:
                HandleDisplaySelect(connectionId, senderClientId, data, state);
                break;

            case MessageTypes.Display.RequestList:
                HandleDisplayListRequest(connectionId, senderClientId);
                break;

            case MessageTypes.Input.MouseMove:
                HandleMouseMove(senderClientId, data, state);
                break;

            case MessageTypes.Input.MouseDown:
                HandleMouseButton(senderClientId, data, state, isDown: true);
                break;

            case MessageTypes.Input.MouseUp:
                HandleMouseButton(senderClientId, data, state, isDown: false);
                break;

            case MessageTypes.Input.MouseWheel:
                HandleMouseWheel(senderClientId, data, state);
                break;

            case MessageTypes.Input.KeyDown:
                HandleKey(data, isDown: true);
                break;

            case MessageTypes.Input.KeyUp:
                HandleKey(data, isDown: false);
                break;
        }
    }

    private void HandleDisplaySelect(string connectionId, string senderClientId, ReadOnlyMemory<byte> data, ConnectionPresenterState state)
    {
        var message = ProtocolSerializer.Deserialize<DisplaySelectMessage>(data);
        state.ViewerDisplaySubscriptions[senderClientId] = message.DisplayId;
        _logger.LogInformation("Viewer {ViewerId} selected display {DisplayId}", senderClientId, message.DisplayId);
    }

    private async void HandleDisplayListRequest(string connectionId, string senderClientId)
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

        await _hubClient.SendMessageToViewers(connectionId, MessageTypes.Display.List, data, [senderClientId]);
        _logger.LogInformation("Sent display list to viewer {ViewerId}", senderClientId);
    }

    private void HandleMouseMove(string senderClientId, ReadOnlyMemory<byte> data, ConnectionPresenterState state)
    {
        var message = ProtocolSerializer.Deserialize<MouseMoveMessage>(data);

        if (state.ViewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
        {
            var display = GetDisplayById(displayId);
            if (display is not null)
            {
                _inputInjectionService.InjectMouseMove(display, message.X, message.Y);
            }
        }
    }

    private void HandleMouseButton(string senderClientId, ReadOnlyMemory<byte> data, ConnectionPresenterState state, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<MouseButtonMessage>(data);

        if (state.ViewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
        {
            var display = GetDisplayById(displayId);
            if (display is not null)
            {
                var button = (WinServ.Services.MouseButton)message.Button;
                _inputInjectionService.InjectMouseButton(display, button, isDown, message.X, message.Y);
            }
        }
    }

    private void HandleMouseWheel(string senderClientId, ReadOnlyMemory<byte> data, ConnectionPresenterState state)
    {
        var message = ProtocolSerializer.Deserialize<MouseWheelMessage>(data);

        if (state.ViewerDisplaySubscriptions.TryGetValue(senderClientId, out var displayId))
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

    public void StartPresenting(string connectionId)
    {
        if (_connections.ContainsKey(connectionId))
        {
            _logger.LogWarning("Already presenting for connection {ConnectionId}", connectionId);
            return;
        }

        var cts = new CancellationTokenSource();
        var state = new ConnectionPresenterState
        {
            CaptureLoopCts = cts,
        };

        if (_connections.TryAdd(connectionId, state))
        {
            state.CaptureLoopTask = Task.Run(() => CaptureLoopAsync(connectionId, state, cts.Token));
            _logger.LogInformation("Started presenting for connection {ConnectionId}", connectionId);

            // Send display list immediately
            _ = SendDisplayListAsync(connectionId);
        }
    }

    public void StopPresenting(string connectionId)
    {
        if (_connections.TryRemove(connectionId, out var state))
        {
            state.CaptureLoopCts.Cancel();
            _logger.LogInformation("Stopped presenting for connection {ConnectionId}", connectionId);
        }
    }

    private async Task SendDisplayListAsync(string connectionId)
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

        await _hubClient.SendMessage(connectionId, MessageTypes.Display.List, data, MessageDestination.AllViewers);
        _logger.LogInformation("Sent display list with {Count} displays", displayInfos.Length);
    }

    private async Task CaptureLoopAsync(string connectionId, ConnectionPresenterState state, CancellationToken ct)
    {
        const int TargetFps = 30;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
        ulong frameNumber = 0;

        _logger.LogInformation("Capture loop started for connection {ConnectionId}", connectionId);

        while (!ct.IsCancellationRequested)
        {
            var frameStart = DateTime.UtcNow;

            try
            {
                await CaptureAndSendFramesAsync(connectionId, state, frameNumber++);
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

        _logger.LogInformation("Capture loop ended for connection {ConnectionId}", connectionId);
    }

    private async Task CaptureAndSendFramesAsync(string connectionId, ConnectionPresenterState state, ulong frameNumber)
    {
        // Group viewers by display
        var viewersByDisplay = state.ViewerDisplaySubscriptions
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
                await _hubClient.SendMessageToViewers(connectionId, MessageTypes.Screen.Frame, data, viewers);
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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _hubClient.ConnectionStarted -= OnConnectionStarted;
        _hubClient.ConnectionChanged -= OnConnectionChanged;
        _hubClient.ConnectionStopped -= OnConnectionStopped;
        _hubClient.MessageReceived -= OnMessageReceived;

        foreach (var connectionId in _connections.Keys.ToList())
        {
            StopPresenting(connectionId);
        }

        GC.SuppressFinalize(this);
    }

    private sealed class ConnectionPresenterState
    {
        public HashSet<string> KnownViewers { get; } = new(); // All viewers that have joined
        public ConcurrentDictionary<string, string> ViewerDisplaySubscriptions { get; } = new(); // viewerClientId -> displayId
        public required CancellationTokenSource CaptureLoopCts { get; init; }
        public Task? CaptureLoopTask { get; set; }
    }
}
#endif

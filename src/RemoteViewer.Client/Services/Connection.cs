using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services;

public sealed class Connection
{
    private readonly ILogger<Connection> _logger;
    private readonly Func<string, ReadOnlyMemory<byte>, MessageDestination, IReadOnlyList<string>?, Task> _sendMessageAsync;
    private readonly Func<Task> _disconnectAsync;
    private readonly IScreenshotService? _screenshotService;

    // Thread-safe viewer list (presenter only)
    private readonly object _viewersLock = new();
    private List<ViewerInfo> _viewers = [];

    // Thread-safe displays list (viewer only)
    private readonly object _displaysLock = new();
    private List<DisplayInfo> _displays = [];

    internal Connection(
        string connectionId,
        bool isPresenter,
        Func<string, ReadOnlyMemory<byte>, MessageDestination, IReadOnlyList<string>?, Task> sendMessageAsync,
        Func<Task> disconnectAsync,
        ILogger<Connection> logger,
        IScreenshotService? screenshotService = null)
    {
        this.ConnectionId = connectionId;
        this.IsPresenter = isPresenter;
        this._screenshotService = screenshotService;
        this._sendMessageAsync = sendMessageAsync;
        this._disconnectAsync = disconnectAsync;
        this._logger = logger;

        // Automatically request display list when connecting as a viewer
        if (!isPresenter)
        {
            var message = new RequestDisplayListMessage();
            var data = ProtocolSerializer.Serialize(message);
            _ = this._sendMessageAsync(MessageTypes.Display.RequestList, data, MessageDestination.PresenterOnly, null);
        }
    }

    public string ConnectionId { get; }
    public bool IsPresenter { get; }
    public bool IsClosed { get; private set; }
    public IReadOnlyList<ViewerInfo> Viewers
    {
        get
        {
            lock (this._viewersLock)
            {
                return this._viewers.ToList().AsReadOnly();
            }
        }
    }
    public IReadOnlyList<DisplayInfo> Displays
    {
        get
        {
            lock (this._displaysLock)
            {
                return this._displays.ToList().AsReadOnly();
            }
        }
    }

    public event EventHandler? Closed;

    private EventHandler? _viewersChanged;
    public event EventHandler? ViewersChanged
    {
        add
        {
            this._viewersChanged += value;

            // Notify new subscriber of current state if viewers already exist
            lock (this._viewersLock)
            {
                if (this._viewers.Count > 0)
                {
                    value?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        remove => this._viewersChanged -= value;
    }

    public event EventHandler<InputReceivedEventArgs>? InputReceived;

    private EventHandler? _displaysChanged;
    public event EventHandler? DisplaysChanged
    {
        add
        {
            this._displaysChanged += value;

            // Notify new subscriber of current state if displays already exist
            lock (this._displaysLock)
            {
                if (this._displays.Count > 0)
                {
                    value?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        remove => this._displaysChanged -= value;
    }

    public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

    public Task DisconnectAsync()
    {
        if (this.IsClosed)
            return Task.CompletedTask;

        return this._disconnectAsync();
    }

    /// <summary>Viewer-only: Select a display to watch.</summary>
    public async Task SelectDisplayAsync(string displayId)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SelectDisplayAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        var message = new DisplaySelectMessage(displayId);
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.Display.Select, data, MessageDestination.PresenterOnly, null);

        this._logger.LogDebug("Selected display {DisplayId}", displayId);
    }

    /// <summary>Viewer-only: Send input to the presenter.</summary>
    public async Task SendInputAsync(string messageType, ReadOnlyMemory<byte> data)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendInputAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        await this._sendMessageAsync(messageType, data, MessageDestination.PresenterOnly, null);
    }

    /// <summary>Presenter-only: Send a frame to all viewers watching a specific display.</summary>
    public async Task SendFrameAsync(string displayId, ulong frameNumber, long timestamp, FrameCodec codec, int width, int height, byte quality, ReadOnlyMemory<byte> frameData)
    {
        if (!this.IsPresenter)
            throw new InvalidOperationException("SendFrameAsync is only valid for presenters");

        if (this.IsClosed)
            return;

        // Get all viewers watching this display
        IReadOnlyList<string> targetViewerIds;
        lock (this._viewersLock)
        {
            targetViewerIds = this._viewers
                .Where(v => v.SelectedDisplayId == displayId)
                .Select(v => v.ClientId)
                .ToList();
        }

        if (targetViewerIds.Count == 0)
            return;

        var message = new FrameMessage(
            displayId,
            frameNumber,
            timestamp,
            codec,
            width,
            height,
            quality,
            frameData
        );

        var serializedData = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.Screen.Frame, serializedData, MessageDestination.SpecificClients, targetViewerIds);
    }

    internal void OnViewersChanged(IReadOnlyList<string> viewerClientIds)
    {
        if (!this.IsPresenter)
            return;

        lock (this._viewersLock)
        {
            // Preserve existing display selections for viewers that are still connected
            var existingSelections = this._viewers.ToDictionary(v => v.ClientId, v => v.SelectedDisplayId);

            this._viewers = viewerClientIds
                .Select(id => new ViewerInfo(id, existingSelections.GetValueOrDefault(id)))
                .ToList();
        }

        this._logger.LogDebug("Viewers changed: {ViewerCount} viewer(s)", viewerClientIds.Count);
        this._viewersChanged?.Invoke(this, EventArgs.Empty);
    }
    internal async void OnMessageReceived(string senderClientId, string messageType, ReadOnlyMemory<byte> data)
    {
        try
        {
            switch (messageType)
            {
                // Presenter-side messages (from viewers)
                case MessageTypes.Display.Select:
                    this.HandleDisplaySelect(senderClientId, data);
                    break;

                case MessageTypes.Display.RequestList:
                    await this.HandleDisplayListRequest(senderClientId);
                    break;

                case MessageTypes.Input.MouseMove:
                    this.HandleMouseMove(senderClientId, data);
                    break;

                case MessageTypes.Input.MouseDown:
                    this.HandleMouseButton(senderClientId, data, isDown: true);
                    break;

                case MessageTypes.Input.MouseUp:
                    this.HandleMouseButton(senderClientId, data, isDown: false);
                    break;

                case MessageTypes.Input.MouseWheel:
                    this.HandleMouseWheel(senderClientId, data);
                    break;

                case MessageTypes.Input.KeyDown:
                    this.HandleKey(senderClientId, data, isDown: true);
                    break;

                case MessageTypes.Input.KeyUp:
                    this.HandleKey(senderClientId, data, isDown: false);
                    break;

                // Viewer-side messages (from presenter)
                case MessageTypes.Display.List:
                    this.HandleDisplayList(data);
                    break;

                case MessageTypes.Screen.Frame:
                    this.HandleFrame(data);
                    break;

                default:
                    this._logger.LogWarning("Unknown message type: {MessageType}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling message {MessageType} from {SenderClientId}", messageType, senderClientId);
        }
    }
    internal void OnClosed()
    {
        this.IsClosed = true;
        this._logger.LogDebug("Connection closed: {ConnectionId}", this.ConnectionId);
        this.Closed?.Invoke(this, EventArgs.Empty);
    }

    private void HandleDisplaySelect(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<DisplaySelectMessage>(data);

        lock (this._viewersLock)
        {
            var index = this._viewers.FindIndex(v => v.ClientId == senderClientId);
            if (index >= 0)
            {
                this._viewers[index] = new ViewerInfo(senderClientId, message.DisplayId);
            }
            else
            {
                this._viewers.Add(new ViewerInfo(senderClientId, message.DisplayId));
            }
        }

        this._logger.LogDebug("Viewer {ViewerId} selected display {DisplayId}", senderClientId, message.DisplayId);
        this._viewersChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task HandleDisplayListRequest(string senderClientId)
    {
        if (this._screenshotService is null)
        {
            this._logger.LogWarning("Cannot respond to display list request - no screenshot service available");
            return;
        }

        var displays = this._screenshotService.GetDisplays();
        var displayInfos = displays
            .Select(d => new DisplayInfo(
                d.Name,
                d.Name,
                d.IsPrimary,
                d.Bounds.Left,
                d.Bounds.Top,
                d.Bounds.Width,
                d.Bounds.Height
            ))
            .ToArray();

        var message = new DisplayListMessage(displayInfos);
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.Display.List, data, MessageDestination.SpecificClients, [senderClientId]);

        this._logger.LogDebug("Sent display list to viewer {ViewerId}", senderClientId);
    }
    private void HandleMouseMove(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<MouseMoveMessage>(data);
        var displayId = this.GetViewerDisplayId(senderClientId);

        var args = new InputReceivedEventArgs(
            senderClientId,
            displayId,
            InputType.MouseMove,
            x: message.X,
            y: message.Y);

        this.InputReceived?.Invoke(this, args);
    }
    private void HandleMouseButton(string senderClientId, ReadOnlyMemory<byte> data, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<MouseButtonMessage>(data);
        var displayId = this.GetViewerDisplayId(senderClientId);

        var args = new InputReceivedEventArgs(
            senderClientId,
            displayId,
            isDown ? InputType.MouseDown : InputType.MouseUp,
            x: message.X,
            y: message.Y,
            button: message.Button);

        this.InputReceived?.Invoke(this, args);
    }
    private void HandleMouseWheel(string senderClientId, ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<MouseWheelMessage>(data);
        var displayId = this.GetViewerDisplayId(senderClientId);

        var args = new InputReceivedEventArgs(
            senderClientId,
            displayId,
            InputType.MouseWheel,
            x: message.X,
            y: message.Y,
            deltaX: message.DeltaX,
            deltaY: message.DeltaY);

        this.InputReceived?.Invoke(this, args);
    }
    private void HandleKey(string senderClientId, ReadOnlyMemory<byte> data, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<KeyMessage>(data);
        var displayId = this.GetViewerDisplayId(senderClientId);

        var args = new InputReceivedEventArgs(
            senderClientId,
            displayId,
            isDown ? InputType.KeyDown : InputType.KeyUp,
            keyCode: message.KeyCode,
            scanCode: message.ScanCode,
            modifiers: message.Modifiers,
            isExtendedKey: message.IsExtendedKey);

        this.InputReceived?.Invoke(this, args);
    }
    private void HandleDisplayList(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<DisplayListMessage>(data);

        lock (this._displaysLock)
        {
            this._displays = [.. message.Displays];
        }

        this._logger.LogDebug("Received display list: {DisplayCount} display(s)", message.Displays.Length);
        this._displaysChanged?.Invoke(this, EventArgs.Empty);
    }
    private void HandleFrame(ReadOnlyMemory<byte> data)
    {
        var message = ProtocolSerializer.Deserialize<FrameMessage>(data);

        var args = new FrameReceivedEventArgs(
            message.DisplayId,
            message.FrameNumber,
            message.Timestamp,
            message.Codec,
            message.Width,
            message.Height,
            message.Quality,
            message.Data);

        this.FrameReceived?.Invoke(this, args);
    }
    private string? GetViewerDisplayId(string viewerClientId)
    {
        lock (this._viewersLock)
        {
            return this._viewers.FirstOrDefault(v => v.ClientId == viewerClientId)?.SelectedDisplayId;
        }
    }
}

/// <summary>
/// Information about a connected viewer, including their selected display.
/// </summary>
public sealed record ViewerInfo(string ClientId, string? SelectedDisplayId);

/// <summary>
/// Type of input event received from a viewer.
/// </summary>
public enum InputType
{
    MouseMove,
    MouseDown,
    MouseUp,
    MouseWheel,
    KeyDown,
    KeyUp
}

/// <summary>
/// Event args for input events received from viewers (presenter-side).
/// </summary>
public sealed class InputReceivedEventArgs : EventArgs
{
    public InputReceivedEventArgs(
        string senderClientId,
        string? displayId,
        InputType type,
        float? x = null,
        float? y = null,
        MouseButton? button = null,
        float? deltaX = null,
        float? deltaY = null,
        ushort? keyCode = null,
        ushort? scanCode = null,
        KeyModifiers? modifiers = null,
        bool? isExtendedKey = null)
    {
        this.SenderClientId = senderClientId;
        this.DisplayId = displayId;
        this.Type = type;
        this.X = x;
        this.Y = y;
        this.Button = button;
        this.DeltaX = deltaX;
        this.DeltaY = deltaY;
        this.KeyCode = keyCode;
        this.ScanCode = scanCode;
        this.Modifiers = modifiers;
        this.IsExtendedKey = isExtendedKey;
    }

    public string SenderClientId { get; }
    public string? DisplayId { get; }
    public InputType Type { get; }

    // Mouse data (when applicable)
    public float? X { get; }
    public float? Y { get; }
    public MouseButton? Button { get; }
    public float? DeltaX { get; }
    public float? DeltaY { get; }

    // Key data (when applicable)
    public ushort? KeyCode { get; }
    public ushort? ScanCode { get; }
    public KeyModifiers? Modifiers { get; }
    public bool? IsExtendedKey { get; }
}

/// <summary>
/// Event args for frame events received from the presenter (viewer-side).
/// </summary>
public sealed class FrameReceivedEventArgs : EventArgs
{
    public FrameReceivedEventArgs(
        string displayId,
        ulong frameNumber,
        long timestamp,
        FrameCodec codec,
        int width,
        int height,
        byte quality,
        ReadOnlyMemory<byte> data)
    {
        this.DisplayId = displayId;
        this.FrameNumber = frameNumber;
        this.Timestamp = timestamp;
        this.Codec = codec;
        this.Width = width;
        this.Height = height;
        this.Quality = quality;
        this.Data = data;
    }

    public string DisplayId { get; }
    public ulong FrameNumber { get; }
    public long Timestamp { get; }
    public FrameCodec Codec { get; }
    public int Width { get; }
    public int Height { get; }
    public byte Quality { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

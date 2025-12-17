using System.Threading;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class Connection
{
    private readonly ILogger<Connection> _logger;
    private readonly Func<string, ReadOnlyMemory<byte>, MessageDestination, IReadOnlyList<string>?, Task> _sendMessageAsync;
    private readonly Func<Task> _disconnectAsync;
    private readonly IDisplayService? _displayService;
    private readonly IScreenshotService? _screenshotService;

    private readonly Lock _participantsLock = new();
    private ClientInfo? _presenter;
    private List<ViewerInfo> _viewers = [];


    internal Connection(
        string connectionId,
        bool isPresenter,
        Func<string, ReadOnlyMemory<byte>, MessageDestination, IReadOnlyList<string>?, Task> sendMessageAsync,
        Func<Task> disconnectAsync,
        ILogger<Connection> logger,
        IDisplayService? displayService = null,
        IScreenshotService? screenshotService = null)
    {
        this.ConnectionId = connectionId;
        this.IsPresenter = isPresenter;
        this._displayService = displayService;
        this._screenshotService = screenshotService;
        this._sendMessageAsync = sendMessageAsync;
        this._disconnectAsync = disconnectAsync;
        this._logger = logger;
    }

    public string ConnectionId { get; }
    public bool IsPresenter { get; }
    public bool IsClosed { get; private set; }
    public ClientInfo? Presenter
    {
        get
        {
            using (this._participantsLock.EnterScope())
            {
                return this._presenter;
            }
        }
    }
    public IReadOnlyList<ViewerInfo> Viewers
    {
        get
        {
            using (this._participantsLock.EnterScope())
            {
                return this._viewers.ToList().AsReadOnly();
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
            using (this._participantsLock.EnterScope())
            {
                if (this._viewers.Count > 0)
                {
                    value?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        remove => this._viewersChanged -= value;
    }

    private EventHandler? _participantsChanged;
    public event EventHandler? ParticipantsChanged
    {
        add
        {
            this._participantsChanged += value;

            // Notify new subscriber of current state if participants already exist
            using (this._participantsLock.EnterScope())
            {
                if (this._presenter is not null || this._viewers.Count > 0)
                {
                    value?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        remove => this._participantsChanged -= value;
    }

    public event EventHandler<InputReceivedEventArgs>? InputReceived;

    public event EventHandler<FrameReceivedEventArgs>? FrameReceived;

    // File transfer events
    public event EventHandler<FileSendRequestReceivedEventArgs>? FileSendRequestReceived;
    public event EventHandler<FileSendResponseReceivedEventArgs>? FileSendResponseReceived;
    public event EventHandler<FileChunkReceivedEventArgs>? FileChunkReceived;
    public event EventHandler<FileCompleteReceivedEventArgs>? FileCompleteReceived;
    public event EventHandler<FileCancelReceivedEventArgs>? FileCancelReceived;
    public event EventHandler<FileErrorReceivedEventArgs>? FileErrorReceived;

    public Task DisconnectAsync()
    {
        if (this.IsClosed)
            return Task.CompletedTask;

        return this._disconnectAsync();
    }

    /// <summary>Viewer-only: Request to switch to the next display.</summary>
    public async Task SwitchDisplayAsync()
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SwitchDisplayAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        var message = new SwitchDisplayMessage();
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.Display.Switch, data, MessageDestination.PresenterOnly, null);

        this._logger.LogDebug("Requested display switch");
    }

    /// <summary>Viewer-only: Send input to the presenter.</summary>
    public async Task SendInputAsync(string messageType, byte[] data)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendInputAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        await this._sendMessageAsync(messageType, data, MessageDestination.PresenterOnly, null);
    }

    /// <summary>Presenter-only: Send a frame to all viewers watching a specific display.</summary>
    public async Task SendFrameAsync(
        string displayId,
        ulong frameNumber,
        FrameCodec codec,
        FrameRegion[] regions)
    {
        if (!this.IsPresenter)
            throw new InvalidOperationException("SendFrameAsync is only valid for presenters");

        if (this.IsClosed)
            return;

        // Get all viewers watching this display
        IReadOnlyList<string> targetViewerIds;
        using (this._participantsLock.EnterScope())
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
            codec,
            regions
        );

        var serializedData = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.Screen.Frame, serializedData, MessageDestination.SpecificClients, targetViewerIds);
    }

    // File transfer methods (Viewer-side)
    public async Task SendFileSendRequestAsync(string transferId, string fileName, long fileSize)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendFileSendRequestAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        var message = new FileSendRequestMessage(transferId, fileName, fileSize);
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.FileTransfer.SendRequest, data, MessageDestination.PresenterOnly, null);
    }

    public async Task SendFileChunkAsync(FileChunkMessage chunk)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendFileChunkAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        var data = ProtocolSerializer.Serialize(chunk);
        await this._sendMessageAsync(MessageTypes.FileTransfer.Chunk, data, MessageDestination.PresenterOnly, null);
    }

    public async Task SendFileCompleteAsync(string transferId)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendFileCompleteAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        var message = new FileCompleteMessage(transferId);
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.FileTransfer.Complete, data, MessageDestination.PresenterOnly, null);
    }

    public async Task SendFileCancelAsync(string transferId, string reason)
    {
        if (this.IsClosed)
            return;

        var message = new FileCancelMessage(transferId, reason);
        var data = ProtocolSerializer.Serialize(message);
        var destination = this.IsPresenter ? MessageDestination.AllViewers : MessageDestination.PresenterOnly;
        await this._sendMessageAsync(MessageTypes.FileTransfer.Cancel, data, destination, null);
    }

    // File transfer methods (Presenter-side)
    public async Task SendFileSendResponseAsync(string transferId, bool accepted, string? error, string senderClientId)
    {
        if (!this.IsPresenter)
            throw new InvalidOperationException("SendFileSendResponseAsync is only valid for presenters");

        if (this.IsClosed)
            return;

        var message = new FileSendResponseMessage(transferId, accepted, error);
        var data = ProtocolSerializer.Serialize(message);
        await this._sendMessageAsync(MessageTypes.FileTransfer.SendResponse, data, MessageDestination.SpecificClients, [senderClientId]);
    }

    public async Task SendFileErrorAsync(string transferId, string error)
    {
        if (this.IsClosed)
            return;

        var message = new FileErrorMessage(transferId, error);
        var data = ProtocolSerializer.Serialize(message);
        var destination = this.IsPresenter ? MessageDestination.AllViewers : MessageDestination.PresenterOnly;
        await this._sendMessageAsync(MessageTypes.FileTransfer.Error, data, destination, null);
    }

    internal void OnConnectionChanged(ConnectionInfo connectionInfo)
    {
        // Get primary display ID for new viewers (presenter-side only)
        var primaryDisplayId = this._displayService?.GetDisplays()
            .FirstOrDefault(d => d.IsPrimary)?.Name;

        using (this._participantsLock.EnterScope())
        {
            // Store presenter info
            this._presenter = connectionInfo.Presenter;

            // Preserve existing display selections for viewers that are still connected,
            // assign primary display to new viewers
            var existingSelections = this._viewers.ToDictionary(v => v.ClientId, v => v.SelectedDisplayId);

            this._viewers = connectionInfo.Viewers
                .Select(v => new ViewerInfo(
                    v.ClientId,
                    existingSelections.GetValueOrDefault(v.ClientId, primaryDisplayId),
                    v.DisplayName))
                .ToList();
        }

        this._logger.LogDebug("Participants changed: presenter={PresenterName}, {ViewerCount} viewer(s)", connectionInfo.Presenter.DisplayName, connectionInfo.Viewers.Count);
        this._viewersChanged?.Invoke(this, EventArgs.Empty);
        this._participantsChanged?.Invoke(this, EventArgs.Empty);
    }
    internal void OnMessageReceived(string senderClientId, string messageType, byte[] data)
    {
        try
        {
            switch (messageType)
            {
                // Presenter-side messages (from viewers)
                case MessageTypes.Display.Switch:
                    this.HandleSwitchDisplay(senderClientId);
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
                case MessageTypes.Screen.Frame:
                    this.HandleFrame(data);
                    break;

                // File transfer messages (Presenter receives from Viewer)
                case MessageTypes.FileTransfer.SendRequest:
                    this.HandleFileSendRequest(senderClientId, data);
                    break;

                case MessageTypes.FileTransfer.Chunk:
                    this.HandleFileChunk(senderClientId, data);
                    break;

                case MessageTypes.FileTransfer.Complete:
                    this.HandleFileComplete(senderClientId, data);
                    break;

                // File transfer messages (Viewer receives from Presenter)
                case MessageTypes.FileTransfer.SendResponse:
                    this.HandleFileSendResponse(data);
                    break;

                // Bidirectional file transfer messages
                case MessageTypes.FileTransfer.Cancel:
                    this.HandleFileCancel(senderClientId, data);
                    break;

                case MessageTypes.FileTransfer.Error:
                    this.HandleFileError(senderClientId, data);
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

    private void HandleSwitchDisplay(string senderClientId)
    {
        if (this._displayService is null)
            return;

        var displays = this._displayService.GetDisplays();
        if (displays.Count == 0)
            return;

        string? newDisplayId;
        using (this._participantsLock.EnterScope())
        {
            var viewerIndex = this._viewers.FindIndex(v => v.ClientId == senderClientId);
            if (viewerIndex < 0)
                return;

            var viewer = this._viewers[viewerIndex];

            // Find current display index
            var currentDisplayIndex = displays
                .Select((d, i) => (Display: d, Index: i))
                .FirstOrDefault(x => x.Display.Name == viewer.SelectedDisplayId)
                .Index;

            // Cycle to next display
            var nextDisplayIndex = (currentDisplayIndex + 1) % displays.Count;
            newDisplayId = displays[nextDisplayIndex].Name;

            this._viewers[viewerIndex] = viewer with { SelectedDisplayId = newDisplayId };
        }

        this._logger.LogDebug("Viewer {ViewerId} switched to display {DisplayId}", senderClientId, newDisplayId);
        this._viewersChanged?.Invoke(this, EventArgs.Empty);

        // Force immediate keyframe so viewer doesn't see black screen
        this._screenshotService?.ForceKeyframe(newDisplayId);
    }
    private void HandleMouseMove(string senderClientId, byte[] data)
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
    private void HandleMouseButton(string senderClientId, byte[] data, bool isDown)
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
    private void HandleMouseWheel(string senderClientId, byte[] data)
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
    private void HandleKey(string senderClientId, byte[] data, bool isDown)
    {
        var message = ProtocolSerializer.Deserialize<KeyMessage>(data);
        var displayId = this.GetViewerDisplayId(senderClientId);

        var args = new InputReceivedEventArgs(
            senderClientId,
            displayId,
            isDown ? InputType.KeyDown : InputType.KeyUp,
            keyCode: message.KeyCode,
            modifiers: message.Modifiers);

        this.InputReceived?.Invoke(this, args);
    }
    private void HandleFrame(byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FrameMessage>(data);

        var args = new FrameReceivedEventArgs(
            message.DisplayId,
            message.FrameNumber,
            message.Codec,
            message.Regions);

        this.FrameReceived?.Invoke(this, args);
    }
    private string? GetViewerDisplayId(string viewerClientId)
    {
        using (this._participantsLock.EnterScope())
        {
            return this._viewers.FirstOrDefault(v => v.ClientId == viewerClientId)?.SelectedDisplayId;
        }
    }

    // File transfer handlers
    private void HandleFileSendRequest(string senderClientId, byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileSendRequestMessage>(data);
        this.FileSendRequestReceived?.Invoke(this, new FileSendRequestReceivedEventArgs(
            senderClientId,
            message.TransferId,
            message.FileName,
            message.FileSize));
    }

    private void HandleFileSendResponse(byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileSendResponseMessage>(data);
        this.FileSendResponseReceived?.Invoke(this, new FileSendResponseReceivedEventArgs(
            message.TransferId,
            message.Accepted,
            message.ErrorMessage));
    }

    private void HandleFileChunk(string senderClientId, byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileChunkMessage>(data);
        this.FileChunkReceived?.Invoke(this, new FileChunkReceivedEventArgs(
            senderClientId,
            message));
    }

    private void HandleFileComplete(string senderClientId, byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileCompleteMessage>(data);
        this.FileCompleteReceived?.Invoke(this, new FileCompleteReceivedEventArgs(
            senderClientId,
            message.TransferId));
    }

    private void HandleFileCancel(string senderClientId, byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileCancelMessage>(data);
        this.FileCancelReceived?.Invoke(this, new FileCancelReceivedEventArgs(
            senderClientId,
            message.TransferId,
            message.Reason));
    }

    private void HandleFileError(string senderClientId, byte[] data)
    {
        var message = ProtocolSerializer.Deserialize<FileErrorMessage>(data);
        this.FileErrorReceived?.Invoke(this, new FileErrorReceivedEventArgs(
            senderClientId,
            message.TransferId,
            message.ErrorMessage));
    }
}

/// <summary>
/// Information about a connected viewer, including their selected display.
/// </summary>
public sealed record ViewerInfo(string ClientId, string? SelectedDisplayId, string DisplayName);

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
        KeyModifiers? modifiers = null)
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
        this.Modifiers = modifiers;
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
    public KeyModifiers? Modifiers { get; }
}

/// <summary>
/// Event args for frame events received from the presenter (viewer-side).
/// </summary>
public sealed class FrameReceivedEventArgs : EventArgs
{
    public FrameReceivedEventArgs(
        string displayId,
        ulong frameNumber,
        FrameCodec codec,
        FrameRegion[] regions)
    {
        this.DisplayId = displayId;
        this.FrameNumber = frameNumber;
        this.Codec = codec;
        this.Regions = regions;
    }

    public string DisplayId { get; }
    public ulong FrameNumber { get; }
    public FrameCodec Codec { get; }
    public FrameRegion[] Regions { get; }
}

// File transfer event args
public sealed class FileSendRequestReceivedEventArgs : EventArgs
{
    public FileSendRequestReceivedEventArgs(string senderClientId, string transferId, string fileName, long fileSize)
    {
        this.SenderClientId = senderClientId;
        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;
    }

    public string SenderClientId { get; }
    public string TransferId { get; }
    public string FileName { get; }
    public long FileSize { get; }
}

public sealed class FileSendResponseReceivedEventArgs : EventArgs
{
    public FileSendResponseReceivedEventArgs(string transferId, bool accepted, string? errorMessage)
    {
        this.TransferId = transferId;
        this.Accepted = accepted;
        this.ErrorMessage = errorMessage;
    }

    public string TransferId { get; }
    public bool Accepted { get; }
    public string? ErrorMessage { get; }
}

public sealed class FileChunkReceivedEventArgs : EventArgs
{
    public FileChunkReceivedEventArgs(string senderClientId, FileChunkMessage chunk)
    {
        this.SenderClientId = senderClientId;
        this.Chunk = chunk;
    }

    public string SenderClientId { get; }
    public FileChunkMessage Chunk { get; }
}

public sealed class FileCompleteReceivedEventArgs : EventArgs
{
    public FileCompleteReceivedEventArgs(string senderClientId, string transferId)
    {
        this.SenderClientId = senderClientId;
        this.TransferId = transferId;
    }

    public string SenderClientId { get; }
    public string TransferId { get; }
}

public sealed class FileCancelReceivedEventArgs : EventArgs
{
    public FileCancelReceivedEventArgs(string senderClientId, string transferId, string reason)
    {
        this.SenderClientId = senderClientId;
        this.TransferId = transferId;
        this.Reason = reason;
    }

    public string SenderClientId { get; }
    public string TransferId { get; }
    public string Reason { get; }
}

public sealed class FileErrorReceivedEventArgs : EventArgs
{
    public FileErrorReceivedEventArgs(string senderClientId, string transferId, string errorMessage)
    {
        this.SenderClientId = senderClientId;
        this.TransferId = transferId;
        this.ErrorMessage = errorMessage;
    }

    public string SenderClientId { get; }
    public string TransferId { get; }
    public string ErrorMessage { get; }
}

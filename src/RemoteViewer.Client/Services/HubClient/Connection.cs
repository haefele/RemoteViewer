using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.SessionRecorderIpc;
using RemoteViewer.Client.Services.WinServiceIpc;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

#pragma warning disable CA1001 // Disposal is handled via OnClosed() which is called externally
public sealed class Connection : IConnectionImpl
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<Connection> _logger;

    private readonly Lock _participantsLock = new();
    private ClientInfo? _presenter;
    private List<ClientInfo> _viewers = [];

    private readonly Lock _connectionPropertiesLock = new();
    private ConnectionProperties _lastSentProperties = new(CanSendSecureAttentionSequence: false, InputBlockedViewerIds: [], AvailableDisplays: []);

    private readonly SessionRecorderRpcClient? _sessionRecorderRpcClient;
    private readonly WinServiceRpcClient? _winServiceRpcClient;
    private Task? _sessionRecorderAuthTask;
    private Task? _winServiceAuthTask;

    public Connection(
        ConnectionHubClient owner,
        string connectionId,
        bool isPresenter,
        IServiceProvider serviceProvider,
        ILogger<Connection> logger)
    {
        this.ConnectionId = connectionId;
        this.IsPresenter = isPresenter;
        this.Owner = owner;

        this._serviceProvider = serviceProvider;
        this._logger = logger;

        this.FileTransfers = ActivatorUtilities.CreateInstance<FileTransferService>(this._serviceProvider, this);
        this.ClipboardSync = ActivatorUtilities.CreateInstance<ClipboardSyncService>(this._serviceProvider, this);
        this.Chat = ActivatorUtilities.CreateInstance<ChatService>(this._serviceProvider, this);
        if (this.IsPresenter)
        {
            this.PresenterCapture = ActivatorUtilities.CreateInstance<PresenterCaptureService>(this._serviceProvider, this);
            this.PresenterService = ActivatorUtilities.CreateInstance<PresenterConnectionService>(this._serviceProvider, this);

            this._sessionRecorderRpcClient = this._serviceProvider.GetRequiredService<SessionRecorderRpcClient>();
            this._sessionRecorderRpcClient.ConnectionStatusChanged += this.OnSessionRecorderConnectionStatusChanged;

            this._winServiceRpcClient = this._serviceProvider.GetRequiredService<WinServiceRpcClient>();
            this._winServiceRpcClient.ConnectionStatusChanged += this.OnWinServiceConnectionStatusChanged;
        }
        else
        {
            this.ViewerService = ActivatorUtilities.CreateInstance<ViewerConnectionService>(this._serviceProvider, this);
        }
    }

    public ConnectionHubClient Owner { get; }

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
    public IReadOnlyList<ClientInfo> Viewers
    {
        get
        {
            using (this._participantsLock.EnterScope())
            {
                return this._viewers.ToList().AsReadOnly();
            }
        }
    }
    public ConnectionProperties ConnectionProperties { get; private set; } = new(CanSendSecureAttentionSequence: false, InputBlockedViewerIds: [], AvailableDisplays: []);

    public FileTransferService FileTransfers { get; }
    public PresenterConnectionService? PresenterService { get; }
    public PresenterConnectionService RequiredPresenterService => this.PresenterService ?? throw new InvalidOperationException("Presenter connection service is not available.");
    public ViewerConnectionService? ViewerService { get; }
    public ViewerConnectionService RequiredViewerService => this.ViewerService ?? throw new InvalidOperationException("Viewer connection service is not available.");
    public PresenterCaptureService? PresenterCapture { get; }
    public ClipboardSyncService ClipboardSync { get; }
    public ChatService Chat { get; }
    public BandwidthTracker BandwidthTracker { get; } = new();

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

    private EventHandler? _connectionPropertiesChanged;
    public event EventHandler? ConnectionPropertiesChanged
    {
        add
        {
            this._connectionPropertiesChanged += value;
            value?.Invoke(this, EventArgs.Empty);
        }
        remove => this._connectionPropertiesChanged -= value;
    }

    public Task DisconnectAsync()
    {
        if (this.IsClosed)
            return Task.CompletedTask;

        return this.Owner.DisconnectAsync(this.ConnectionId);
    }

    async Task IConnectionImpl.SwitchDisplayAsync()
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SwitchDisplayAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.Display.Switch, ReadOnlyMemory<byte>.Empty, MessageDestination.PresenterOnly, null);

        this._logger.LogDebug("Requested display switch");
    }

    async Task IConnectionImpl.SendInputAsync(string messageType, ReadOnlyMemory<byte> data)
    {
        if (this.IsPresenter)
            throw new InvalidOperationException("SendInputAsync is only valid for viewers");

        if (this.IsClosed)
            return;

        await this.Owner.SendMessageAsync(this.ConnectionId, messageType, data, MessageDestination.PresenterOnly, null);
    }

    async Task IConnectionImpl.SendFrameAsync(
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
        var targetViewerIds = this.PresenterService is IPresenterServiceImpl presenterService
            ? await presenterService.GetViewerIdsWatchingDisplayAsync(displayId)
            : [];

        if (targetViewerIds.Count == 0)
            return;

        var message = new FrameMessage(
            displayId,
            frameNumber,
            codec,
            regions
        );

        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);
        this.BandwidthTracker.AddBytes(buffer.WrittenCount);
        await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.Screen.Frame, buffer.WrittenMemory, MessageDestination.SpecificClients, targetViewerIds);
    }

    async Task IConnectionImpl.SendFileSendRequestAsync(string transferId, string fileName, long fileSize, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        var message = new FileSendRequestMessage(transferId, fileName, fileSize);
        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileSendRequestAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.SendRequest, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileSendRequestAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.SendRequest, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async Task IConnectionImpl.SendFileSendResponseAsync(string transferId, bool accepted, string? error, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        var message = new FileSendResponseMessage(transferId, accepted, error);
        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileSendResponseAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.SendResponse, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileSendResponseAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.SendResponse, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async Task IConnectionImpl.SendFileChunkAsync(FileChunkMessage chunk, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, chunk);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileChunkAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Chunk, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileChunkAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Chunk, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async Task IConnectionImpl.SendFileCompleteAsync(string transferId, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        var message = new FileCompleteMessage(transferId);
        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileCompleteAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Complete, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileCompleteAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Complete, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async Task IConnectionImpl.SendFileCancelAsync(string transferId, string reason, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        var message = new FileCancelMessage(transferId, reason);
        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileCancelAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Cancel, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileCancelAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Cancel, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async Task IConnectionImpl.SendFileErrorAsync(string transferId, string error, string? targetClientId)
    {
        if (this.IsClosed)
            return;

        var message = new FileErrorMessage(transferId, error);
        using var buffer = PooledBufferWriter.Rent();
        ProtocolSerializer.Serialize(buffer, message);

        if (targetClientId is not null)
        {
            if (!this.IsPresenter)
                throw new InvalidOperationException("SendFileErrorAsync with targetClientId is only valid for presenters");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Error, buffer.WrittenMemory, MessageDestination.SpecificClients, [targetClientId]);
        }
        else
        {
            if (this.IsPresenter)
                throw new InvalidOperationException("SendFileErrorAsync without targetClientId is only valid for viewers");
            await this.Owner.SendMessageAsync(this.ConnectionId, MessageTypes.FileTransfer.Error, buffer.WrittenMemory, MessageDestination.PresenterOnly, null);
        }
    }

    async void IConnectionImpl.OnConnectionChanged(ConnectionInfo connectionInfo)
    {
        try
        {
            // Participants changed
            {
                using (this._participantsLock.EnterScope())
                {
                    this._presenter = connectionInfo.Presenter;
                    this._viewers = connectionInfo.Viewers.ToList();
                }

                this._logger.LogDebug("Participants changed: presenter={PresenterName}, {ViewerCount} viewer(s)", connectionInfo.Presenter.DisplayName, connectionInfo.Viewers.Count);
                this._viewersChanged?.Invoke(this, EventArgs.Empty);
                this._participantsChanged?.Invoke(this, EventArgs.Empty);
            }

            // Connection properties changed
            {
                bool propertiesChanged;
                using (this._connectionPropertiesLock.EnterScope())
                {
                    propertiesChanged = !AreConnectionPropertiesEqual(this.ConnectionProperties, connectionInfo.Properties);
                    this.ConnectionProperties = connectionInfo.Properties;
                    this._lastSentProperties = connectionInfo.Properties;
                }

                if (propertiesChanged)
                {
                    this._connectionPropertiesChanged?.Invoke(this, EventArgs.Empty);
                    this._logger.LogDebug("Connection properties changed");
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling connection changed event");
        }
    }

    async void IConnectionImpl.OnMessageReceived(string senderClientId, string messageType, byte[] data)
    {
        try
        {
            switch (messageType)
            {
                case MessageTypes.Display.Switch:
                    if (this.PresenterService is IPresenterServiceImpl presenterService)
                    {
                        await presenterService.CycleViewerDisplayAsync(senderClientId);
                    }
                    break;

                case MessageTypes.Display.Select:
                    if (this.PresenterService is IPresenterServiceImpl presenterServiceForSelect)
                    {
                        var displayId = ProtocolSerializer.Deserialize<string>(data);
                        await presenterServiceForSelect.SelectViewerDisplayAsync(senderClientId, displayId);
                    }
                    break;

                case MessageTypes.Input.MouseMove:
                    {
                        var message = ProtocolSerializer.Deserialize<MouseMoveMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleMouseMove(senderClientId, message.X, message.Y);
                        break;
                    }

                case MessageTypes.Input.MouseDown:
                    {
                        var message = ProtocolSerializer.Deserialize<MouseButtonMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleMouseButton(senderClientId, message.X, message.Y, message.Button, isDown: true);
                        break;
                    }

                case MessageTypes.Input.MouseUp:
                    {
                        var message = ProtocolSerializer.Deserialize<MouseButtonMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleMouseButton(senderClientId, message.X, message.Y, message.Button, isDown: false);
                        break;
                    }

                case MessageTypes.Input.MouseWheel:
                    {
                        var message = ProtocolSerializer.Deserialize<MouseWheelMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleMouseWheel(senderClientId, message.X, message.Y, message.DeltaX, message.DeltaY);
                        break;
                    }

                case MessageTypes.Input.KeyDown:
                    {
                        var message = ProtocolSerializer.Deserialize<KeyMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleKey(senderClientId, message.KeyCode, message.Modifiers, isDown: true);
                        break;
                    }

                case MessageTypes.Input.KeyUp:
                    {
                        var message = ProtocolSerializer.Deserialize<KeyMessage>(data);
                        ((IPresenterServiceImpl)this.PresenterService!).HandleKey(senderClientId, message.KeyCode, message.Modifiers, isDown: false);
                        break;
                    }

                case MessageTypes.Input.SecureAttentionSequence:
                    this._logger.LogInformation("Received Ctrl+Alt+Del request from {SenderClientId}", senderClientId);
                    ((IPresenterServiceImpl)this.PresenterService!).HandleSecureAttentionSequence(senderClientId);
                    break;

                case MessageTypes.Screen.Frame:
                    {
                        var message = ProtocolSerializer.Deserialize<FrameMessage>(data);
                        ((IViewerServiceImpl)this.ViewerService!).HandleFrame(message.DisplayId, message.FrameNumber, message.Codec, message.Regions);

                        if (this.Owner.Options.SuppressAutoFrameAck is false)
                            await this.Owner.SendAckFrameAsync();

                        break;
                    }

                case MessageTypes.FileTransfer.SendRequest:
                    {
                        var message = ProtocolSerializer.Deserialize<FileSendRequestMessage>(data);
                        await ((IFileTransferServiceImpl)this.FileTransfers).HandleFileSendRequestAsync(senderClientId, message.TransferId, message.FileName, message.FileSize);
                        break;
                    }

                case MessageTypes.FileTransfer.SendResponse:
                    {
                        var message = ProtocolSerializer.Deserialize<FileSendResponseMessage>(data);
                        ((IFileTransferServiceImpl)this.FileTransfers).HandleFileSendResponse(message.TransferId, message.Accepted, message.ErrorMessage);
                        break;
                    }

                case MessageTypes.FileTransfer.Chunk:
                    {
                        var message = ProtocolSerializer.Deserialize<FileChunkMessage>(data);
                        ((IFileTransferServiceImpl)this.FileTransfers).HandleFileChunk(senderClientId, message);
                        break;
                    }

                case MessageTypes.FileTransfer.Complete:
                    {
                        var message = ProtocolSerializer.Deserialize<FileCompleteMessage>(data);
                        ((IFileTransferServiceImpl)this.FileTransfers).HandleFileComplete(senderClientId, message.TransferId);
                        break;
                    }

                case MessageTypes.FileTransfer.Cancel:
                    {
                        var message = ProtocolSerializer.Deserialize<FileCancelMessage>(data);
                        ((IFileTransferServiceImpl)this.FileTransfers).HandleFileCancel(senderClientId, message.TransferId, message.Reason);
                        break;
                    }

                case MessageTypes.FileTransfer.Error:
                    {
                        var message = ProtocolSerializer.Deserialize<FileErrorMessage>(data);
                        ((IFileTransferServiceImpl)this.FileTransfers).HandleFileError(senderClientId, message.TransferId, message.ErrorMessage);
                        break;
                    }

                case MessageTypes.Clipboard.Text:
                    {
                        var message = ProtocolSerializer.Deserialize<ClipboardTextMessage>(data);
                        await ((IClipboardSyncServiceImpl)this.ClipboardSync).HandleTextMessageAsync(message);
                        break;
                    }

                case MessageTypes.Clipboard.Image:
                    {
                        var message = ProtocolSerializer.Deserialize<ClipboardImageMessage>(data);
                        await ((IClipboardSyncServiceImpl)this.ClipboardSync).HandleImageMessageAsync(message);
                        break;
                    }

                case MessageTypes.Chat.Message:
                    {
                        var message = ProtocolSerializer.Deserialize<ChatMessage>(data);
                        ((IChatServiceImpl)this.Chat).HandleChatMessage(message);
                        break;
                    }

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

    void IConnectionImpl.OnClosed()
    {
        this.IsClosed = true;
        this.FileTransfers.Dispose();
        this.ClipboardSync.Dispose();
        this.Chat.Dispose();
        this.PresenterService?.Dispose();
        this.PresenterCapture?.Dispose();
        this.ViewerService?.Dispose();
        this._logger.LogDebug("Connection closed: {ConnectionId}", this.ConnectionId);
        this.Closed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateConnectionPropertiesAndSend(Func<ConnectionProperties, ConnectionProperties> update)
    {
        ConnectionProperties properties;
        bool shouldSend;

        using (this._connectionPropertiesLock.EnterScope())
        {
            properties = update(this.ConnectionProperties);

            shouldSend = AreConnectionPropertiesEqual(this._lastSentProperties, properties) is false;
            if (shouldSend)
            {
                this._lastSentProperties = properties;
            }
        }

        if (shouldSend)
        {
            await this.Owner.SetConnectionPropertiesAsync(this.ConnectionId, properties);
        }
    }

    private static bool AreConnectionPropertiesEqual(ConnectionProperties left, ConnectionProperties right)
    {
        if (left.CanSendSecureAttentionSequence != right.CanSendSecureAttentionSequence)
            return false;

        var leftIds = left.InputBlockedViewerIds;
        var rightIds = right.InputBlockedViewerIds;
        if (!ReferenceEquals(leftIds, rightIds))
        {
            if (leftIds.Count != rightIds.Count)
                return false;

            var leftSet = new HashSet<string>(leftIds, StringComparer.Ordinal);
            if (!leftSet.SetEquals(rightIds))
                return false;
        }

        var leftDisplays = left.AvailableDisplays;
        var rightDisplays = right.AvailableDisplays;
        if (!ReferenceEquals(leftDisplays, rightDisplays) && !leftDisplays.SequenceEqual(rightDisplays))
            return false;

        return true;
    }

    private void OnSessionRecorderConnectionStatusChanged(object? sender, EventArgs e)
    {
        if (this.IsClosed || !this._sessionRecorderRpcClient!.IsConnected)
            return;
        if (this._sessionRecorderRpcClient.IsAuthenticatedFor(this.ConnectionId))
            return;
        if (this._sessionRecorderAuthTask is { IsCompleted: false })
            return;

        this._sessionRecorderAuthTask = this.AuthenticateSessionRecorderWithRetryAsync();
    }

    private void OnWinServiceConnectionStatusChanged(object? sender, EventArgs e)
    {
        if (this.IsClosed || !this._winServiceRpcClient!.IsConnected)
            return;
        if (this._winServiceRpcClient.IsAuthenticatedFor(this.ConnectionId))
            return;
        if (this._winServiceAuthTask is { IsCompleted: false })
            return;

        this._winServiceAuthTask = this.AuthenticateWinServiceWithRetryAsync();
    }

    private async Task AuthenticateSessionRecorderWithRetryAsync()
    {
        while (this.IsClosed is false && this._sessionRecorderRpcClient!.IsConnected && this._sessionRecorderRpcClient.IsAuthenticatedFor(this.ConnectionId) is false)
        {
            try
            {
                var token = await this.Owner.GenerateIpcAuthTokenAsync(this.ConnectionId);
                if (token is null)
                {
                    this._logger.LogWarning("Failed to get IPC auth token from server for SessionRecorder");
                    await Task.Delay(1000);
                    continue;
                }

                if (await this._sessionRecorderRpcClient.AuthenticateForConnectionAsync(token, this.ConnectionId, CancellationToken.None))
                {
                    this._logger.LogInformation("SessionRecorder IPC authenticated for connection {ConnectionId}", this.ConnectionId);
                    return;
                }

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error during SessionRecorder IPC authentication");
                await Task.Delay(1000);
            }
        }
    }

    private async Task AuthenticateWinServiceWithRetryAsync()
    {
        while (this.IsClosed is false && this._winServiceRpcClient!.IsConnected && this._winServiceRpcClient.IsAuthenticatedFor(this.ConnectionId) is false)
        {
            try
            {
                var token = await this.Owner.GenerateIpcAuthTokenAsync(this.ConnectionId);
                if (token is null)
                {
                    this._logger.LogWarning("Failed to get IPC auth token from server for WinService");
                    await Task.Delay(1000);
                    continue;
                }

                if (await this._winServiceRpcClient.AuthenticateForConnectionAsync(token, this.ConnectionId, CancellationToken.None))
                {
                    this._logger.LogInformation("WinService IPC authenticated for connection {ConnectionId}", this.ConnectionId);
                    return;
                }

                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error during WinService IPC authentication");
                await Task.Delay(1000);
            }
        }
    }
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

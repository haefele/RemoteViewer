using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.AspNetCore.SignalR;
using Orleans.Concurrency;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared.Protocol;
using System.Threading.Channels;


namespace RemoteViewer.Server.Orleans.Grains;

public interface IClientSendGrain : IGrainWithStringKey
{
    Task Enqueue(string connectionId, string senderClientId, string messageType, byte[] data);
    Task AckFrame(string connectionId);
    Task Disconnect();
}

[Reentrant]
[SuppressMessage("IDisposableAnalyzers", "CA1001", Justification = "Orleans grains don't implement IDisposable; cleanup is in OnDeactivateAsync")]
public sealed partial class ClientSendGrain(ILogger<ClientSendGrain> logger, IHubContext<ConnectionHub, IConnectionHubClient> hubContext)
    : Grain, IClientSendGrain
{
    private readonly Channel<QueuedMessage> _nonFrameChannel = Channel.CreateUnbounded<QueuedMessage>(new() { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdownCts = new();

    private readonly ConcurrentDictionary<string, FrameSendState> _frameStates = new(StringComparer.Ordinal);
    private Task? _processingTask;



    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        this._processingTask = Task.Run(() => this.ProcessNonFrameMessagesAsync(this._shutdownCts.Token), cancellationToken);

        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        this._shutdownCts.Cancel();
        this._nonFrameChannel.Writer.TryComplete();

        if (this._processingTask is not null)
        {
            try
            {
                await this._processingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        this._shutdownCts.Dispose();
    }

    public Task Enqueue(string connectionId, string senderClientId, string messageType, byte[] data)
    {
        var message = new QueuedMessage(connectionId, senderClientId, messageType, data);

        if (messageType == MessageTypes.Screen.Frame)
        {
            return this.EnqueueFrame(message);
        }
        else
        {
            this._nonFrameChannel.Writer.TryWrite(message);
            return Task.CompletedTask;
        }
    }

    private Task EnqueueFrame(QueuedMessage message)
    {
        var state = this._frameStates.GetOrAdd(message.ConnectionId, _ => new FrameSendState());

        var (wasIdle, dropped) = state.TryEnqueueOrSend(message);

        if (wasIdle)
        {
            return this.SendFrameAsync(message);
        }

        if (dropped is { } droppedMessage)
        {
            this.LogFrameDropped(droppedMessage.MessageType);
        }

        return Task.CompletedTask;
    }

    public Task AckFrame(string connectionId)
    {
        if (!this._frameStates.TryGetValue(connectionId, out var state))
        {
            return Task.CompletedTask;
        }

        var toSend = state.TryGetPendingAndClearInFlight();

        if (toSend is { } message)
        {
            return this.SendFrameAsync(message);
        }

        this.TryRemoveState(connectionId, state);
        return Task.CompletedTask;
    }

    private void TryRemoveState(string connectionId, FrameSendState state)
    {
        if (state.CanRemove())
        {
            this._frameStates.TryRemove(new KeyValuePair<string, FrameSendState>(connectionId, state));
        }
    }

    public async Task Disconnect()
    {

        this._shutdownCts.Cancel();
        this._nonFrameChannel.Writer.TryComplete();

        if (this._processingTask is not null)
        {
            try
            {
                await this._processingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        this.DeactivateOnIdle();
    }

    private async Task SendFrameAsync(QueuedMessage frame)
    {
        try
        {
            await this.SendAsync(frame);
        }
        catch
        {
            // If delivery fails, clear in-flight state so the next frame can be sent
            if (this._frameStates.TryGetValue(frame.ConnectionId, out var state))
            {
                state.ClearOnError();
                this.TryRemoveState(frame.ConnectionId, state);
            }
        }
    }



    private async Task ProcessNonFrameMessagesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var message in this._nonFrameChannel.Reader.ReadAllAsync(ct))
            {
                await this.SendAsync(message);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private Task SendAsync(QueuedMessage message)
    {
        return hubContext.Clients
            .Client(this.GetPrimaryKeyString())
            .MessageReceived(message.ConnectionId, message.SenderClientId, message.MessageType, message.Data);
    }

    private readonly record struct QueuedMessage(
        string ConnectionId,
        string SenderClientId,
        string MessageType,
        byte[] Data);

    private sealed class FrameSendState
    {
        private readonly Lock _lock = new();
        private bool _inFlight;
        private QueuedMessage? _pendingFrame;

        public (bool wasIdle, QueuedMessage? dropped) TryEnqueueOrSend(QueuedMessage message)
        {
            using (this._lock.EnterScope())
            {
                if (this._inFlight is false)
                {
                    this._inFlight = true;
                    return (wasIdle: true, dropped: null);
                }
                else
                {
                    var dropped = this._pendingFrame;
                    this._pendingFrame = message;
                    return (wasIdle: false, dropped: dropped);
                }
            }
        }

        public QueuedMessage? TryGetPendingAndClearInFlight()
        {
            using (this._lock.EnterScope())
            {
                if (this._pendingFrame is { } pending)
                {
                    this._pendingFrame = null;
                    // Keep _inFlight = true since we're about to send pending
                    return pending;
                }
                else
                {
                    this._inFlight = false;
                    return null;
                }
            }
        }

        public void ClearOnError()
        {
            using (this._lock.EnterScope())
            {
                this._inFlight = false;
                this._pendingFrame = null;
            }
        }

        public bool CanRemove()
        {
            using (this._lock.EnterScope())
            {
                return this._inFlight is false && this._pendingFrame is null;
            }
        }
    }
}


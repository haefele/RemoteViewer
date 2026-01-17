using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using Orleans.Concurrency;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared.Protocol;
using System.Threading.Channels;

namespace RemoteViewer.Server.Orleans.Grains;

public interface IClientSendGrain : IGrainWithStringKey
{
    Task Enqueue(string connectionId, string senderClientId, string messageType, byte[] data);
    Task AckFrame();
    Task Disconnect();
}

[Reentrant]
[SuppressMessage("IDisposableAnalyzers", "CA1001", Justification = "Orleans grains don't implement IDisposable; cleanup is in OnDeactivateAsync")]
public sealed partial class ClientSendGrain(ILogger<ClientSendGrain> logger, IHubContext<ConnectionHub, IConnectionHubClient> hubContext)
    : Grain, IClientSendGrain
{
    private readonly Lock _sync = new();
    private readonly Channel<QueuedMessage> _nonFrameChannel = Channel.CreateUnbounded<QueuedMessage>(new() { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _shutdownCts = new();

    private QueuedMessage? _pendingFrame;
    private bool _frameInFlight;
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
        using (this._sync.EnterScope())
        {
            if (this._frameInFlight is false)
            {
                this._frameInFlight = true;
                return this.SendFrameAsync(message);
            }
            else
            {
                var dropped = this._pendingFrame;
                this._pendingFrame = message;

                if (dropped is not null)
                {
                    this.LogFrameDropped(dropped.Value.MessageType);
                }

                return Task.CompletedTask;
            }
        }
    }

    public Task AckFrame()
    {
        QueuedMessage? toSend = null;

        using (this._sync.EnterScope())
        {
            if (this._pendingFrame is not null)
            {
                toSend = this._pendingFrame;
                this._pendingFrame = null;
            }
            else
            {
                this._frameInFlight = false;
            }
        }

        return toSend is not null
            ? this.SendFrameAsync(toSend.Value)
            : Task.CompletedTask;
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
            using (this._sync.EnterScope())
            {
                this._frameInFlight = false;
                this._pendingFrame = null;
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
}

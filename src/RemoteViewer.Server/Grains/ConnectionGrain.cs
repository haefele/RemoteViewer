using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Server.Grains.Interfaces;
using RemoteViewer.Server.Grains.State;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared;

using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Grains;

public sealed class ConnectionGrain(
    ILogger<ConnectionGrain> logger,
    IHubContext<ConnectionHub, IConnectionHubClient> hubContext,
    IGrainFactory grainFactory) : Grain, IConnectionGrain
{
    private readonly ConnectionGrainState _state = new();

    private string ConnectionId => this.GetPrimaryKeyString();

    public async Task InitializeAsync(string presenterSignalrId)
    {
        if (this._state.PresenterSignalrId is not null)
        {
            logger.LogWarning("ConnectionGrain {ConnectionId} already initialized", this.ConnectionId);
            return;
        }

        this._state.PresenterSignalrId = presenterSignalrId;
        this._state.IsClosed = false;

        logger.LogInformation("Connection initialized: ConnectionId={ConnectionId}, Presenter={PresenterSignalrId}", this.ConnectionId, presenterSignalrId);

        await hubContext.Clients.Client(presenterSignalrId).ConnectionStarted(this.ConnectionId, isPresenter: true);
    }

    public async Task<bool> AddViewerAsync(string viewerSignalrId)
    {
        if (this._state.IsClosed || this._state.PresenterSignalrId is null)
            return false;

        if (this._state.ViewerSignalrIds.Add(viewerSignalrId))
        {
            var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
            await viewerGrain.AddConnectionAsync(this.ConnectionId);

            logger.LogInformation("Viewer added: ConnectionId={ConnectionId}, Viewer={ViewerSignalrId}, ViewerCount={Count}",
                this.ConnectionId, viewerSignalrId, this._state.ViewerSignalrIds.Count);

            await hubContext.Clients.Client(viewerSignalrId).ConnectionStarted(this.ConnectionId, isPresenter: false);
            await this.NotifyConnectionChangedAsync();

            return true;
        }

        logger.LogWarning("Viewer already present: ConnectionId={ConnectionId}, Viewer={ViewerSignalrId}", this.ConnectionId, viewerSignalrId);
        return false;
    }

    public async Task<bool> RemoveClientAsync(string signalrId)
    {
        if (this._state.PresenterSignalrId == signalrId)
        {
            logger.LogInformation("Presenter disconnected: ConnectionId={ConnectionId}, ViewerCount={Count}", this.ConnectionId, this._state.ViewerSignalrIds.Count);

            // Clean up and notify all viewers that connection is closing
            foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
            {
                var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
                await viewerGrain.RemoveConnectionAsync(this.ConnectionId);
                await hubContext.Clients.Client(viewerSignalrId).ConnectionStopped(this.ConnectionId);
            }

            // Notify presenter
            await hubContext.Clients.Client(signalrId).ConnectionStopped(this.ConnectionId);

            this._state.IsClosed = true;
            this.DeactivateOnIdle();

            return true;
        }

        if (this._state.ViewerSignalrIds.Remove(signalrId))
        {
            logger.LogInformation("Viewer disconnected: ConnectionId={ConnectionId}, ViewerCount={Count}", this.ConnectionId, this._state.ViewerSignalrIds.Count);

            this._state.Properties = await this.NormalizePropertiesAsync(this._state.Properties);
            await this.NotifyConnectionChangedAsync();

            await hubContext.Clients.Client(signalrId).ConnectionStopped(this.ConnectionId);
        }

        return false;
    }

    [ReadOnly]
    public Task<bool> IsActiveAsync()
    {
        return Task.FromResult(!this._state.IsClosed && this._state.PresenterSignalrId is not null);
    }

    [ReadOnly]
    public Task<string?> GetPresenterSignalrIdAsync()
    {
        return Task.FromResult(this._state.PresenterSignalrId);
    }

    [ReadOnly]
    public Task<IReadOnlySet<string>> GetViewerSignalrIdsAsync()
    {
        return Task.FromResult<IReadOnlySet<string>>(this._state.ViewerSignalrIds);
    }

    [ReadOnly]
    public Task<ConnectionProperties> GetPropertiesAsync()
    {
        return Task.FromResult(this._state.Properties);
    }

    public async Task<bool> UpdatePropertiesAsync(string requestingSignalrId, ConnectionProperties properties)
    {
        if (this._state.PresenterSignalrId != requestingSignalrId)
        {
            logger.LogWarning("Non-presenter tried to update properties: ConnectionId={ConnectionId}, Requester={Requester}", this.ConnectionId, requestingSignalrId);
            return false;
        }

        this._state.Properties = await this.NormalizePropertiesAsync(properties);
        await this.NotifyConnectionChangedAsync();

        return true;
    }

    [ReadOnly]
    public Task<bool> IsPresenterAsync(string signalrId)
    {
        return Task.FromResult(this._state.PresenterSignalrId == signalrId);
    }

    public async Task<bool> SendMessageAsync(string senderSignalrId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds)
    {
        var isSenderPresenter = this._state.PresenterSignalrId == senderSignalrId;
        var isSenderViewer = this._state.ViewerSignalrIds.Contains(senderSignalrId);

        if (!isSenderPresenter && !isSenderViewer)
            return false;

        var senderGrain = grainFactory.GetGrain<IClientGrain>(senderSignalrId);
        var senderClientId = await senderGrain.GetClientIdAsync();

        switch (destination)
        {
            case MessageDestination.PresenterOnly:
                if (isSenderViewer && this._state.PresenterSignalrId is not null)
                {
                    await hubContext.Clients.Client(this._state.PresenterSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                }
                break;

            case MessageDestination.AllViewers:
                foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
                {
                    await hubContext.Clients.Client(viewerSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                }
                break;

            case MessageDestination.All:
                if (this._state.PresenterSignalrId is not null)
                {
                    await hubContext.Clients.Client(this._state.PresenterSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                }
                foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
                {
                    await hubContext.Clients.Client(viewerSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                }
                break;

            case MessageDestination.AllExceptSender:
                if (this._state.PresenterSignalrId is not null && this._state.PresenterSignalrId != senderSignalrId)
                {
                    await hubContext.Clients.Client(this._state.PresenterSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                }
                foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
                {
                    if (viewerSignalrId != senderSignalrId)
                    {
                        await hubContext.Clients.Client(viewerSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                    }
                }
                break;

            case MessageDestination.SpecificClients:
                if (targetClientIds is null || targetClientIds.Count == 0)
                    break;

                var targetClientIdSet = targetClientIds.ToHashSet(StringComparer.Ordinal);

                if (this._state.PresenterSignalrId is not null)
                {
                    var presenterGrain = grainFactory.GetGrain<IClientGrain>(this._state.PresenterSignalrId);
                    var presenterClientId = await presenterGrain.GetClientIdAsync();
                    if (targetClientIdSet.Contains(presenterClientId))
                    {
                        await hubContext.Clients.Client(this._state.PresenterSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                    }
                }

                foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
                {
                    var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
                    var viewerClientId = await viewerGrain.GetClientIdAsync();
                    if (targetClientIdSet.Contains(viewerClientId))
                    {
                        await hubContext.Clients.Client(viewerSignalrId).MessageReceived(this.ConnectionId, senderClientId, messageType, data);
                    }
                }
                break;
        }

        return true;
    }

    private async Task NotifyConnectionChangedAsync()
    {
        if (this._state.PresenterSignalrId is null)
            return;

        var presenterGrain = grainFactory.GetGrain<IClientGrain>(this._state.PresenterSignalrId);
        var presenterInfo = await presenterGrain.GetClientInfoAsync();

        var viewerInfos = new List<ClientInfo>();
        foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
        {
            var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
            viewerInfos.Add(await viewerGrain.GetClientInfoAsync());
        }

        var connectionInfo = new ConnectionInfo(this.ConnectionId, presenterInfo, viewerInfos, this._state.Properties);

        await hubContext.Clients.Client(this._state.PresenterSignalrId).ConnectionChanged(connectionInfo);
        foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
        {
            await hubContext.Clients.Client(viewerSignalrId).ConnectionChanged(connectionInfo);
        }
    }

    private async Task<ConnectionProperties> NormalizePropertiesAsync(ConnectionProperties properties)
    {
        var viewerClientIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var viewerSignalrId in this._state.ViewerSignalrIds)
        {
            var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
            var clientId = await viewerGrain.GetClientIdAsync();
            if (!string.IsNullOrEmpty(clientId))
                viewerClientIds.Add(clientId);
        }

        var blockedIds = properties.InputBlockedViewerIds
            .Where(id => viewerClientIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return properties with { InputBlockedViewerIds = blockedIds };
    }
}

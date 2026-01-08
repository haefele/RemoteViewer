using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared;

using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Grains;

public interface IConnectionGrain : IGrainWithStringKey
{
    Task UpdateProperties(string signalrConnectionId, ConnectionProperties properties);
    Task SendMessage(string senderSignalrConnectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds);
    Task<bool> IsPresenter(string signalrConnectionId);

    Task Internal_InitializePresenter(IClientGrain presenter);
    Task Internal_AddViewer(IClientGrain viewer);
    Task Internal_RemoveClient(IClientGrain client);
    Task Internal_DisplayNameChanged();
}

public sealed class ConnectionGrain(IHubContext<ConnectionHub, IConnectionHubClient> hubContext)
    : Grain, IConnectionGrain
{
    private IClientGrain? _presenter;
    private readonly HashSet<IClientGrain> _viewers = new();
    private ConnectionProperties _properties = new(false, [], []);

    public async Task UpdateProperties(string signalrConnectionId, ConnectionProperties properties)
    {
        if (this._presenter?.GetPrimaryKeyString() != signalrConnectionId)
            return;

        this._properties = properties;
        await this.NotifyConnectionChangedAsync();
    }
    public async Task SendMessage(string senderSignalrConnectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds)
    {
        this.EnsureInitialized();

        // Get sender's clientId from local state - no cross-grain calls
        string? senderClientId = null;
        if (this._presenter.GetPrimaryKeyString() == senderSignalrConnectionId)
        {
            senderClientId = await this._presenter.Internal_GetClientId();
        }
        else if (this._viewers.FirstOrDefault(f => f.GetPrimaryKeyString() == senderSignalrConnectionId) is { } viewer)
        {
            senderClientId = await viewer.Internal_GetClientId();
        }

        if (senderClientId is null)
            return;

        var isSenderPresenter = this._presenter?.GetPrimaryKeyString() == senderSignalrConnectionId;

        switch (destination)
        {
            case MessageDestination.PresenterOnly:
                if (!isSenderPresenter && this._presenter is not null)
                {
                    await hubContext.Clients.Client(this._presenter.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }
                break;

            case MessageDestination.AllViewers:
                foreach (var viewer in this._viewers)
                {
                    await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }
                break;

            case MessageDestination.All:
                if (this._presenter is not null)
                {
                    await hubContext.Clients.Client(this._presenter.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }
                foreach (var viewer in this._viewers)
                {
                    await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }
                break;

            case MessageDestination.AllExceptSender:
                if (this._presenter is not null && this._presenter.GetPrimaryKeyString() != senderSignalrConnectionId)
                {
                    await hubContext.Clients.Client(this._presenter.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }
                foreach (var viewer in this._viewers)
                {
                    if (viewer.GetPrimaryKeyString() != senderSignalrConnectionId)
                    {
                        await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                    }
                }
                break;

            case MessageDestination.SpecificClients:
                if (targetClientIds is null || targetClientIds.Count == 0)
                    break;

                var targetClientIdSet = targetClientIds.ToHashSet(StringComparer.Ordinal);

                if (this._presenter is not null && targetClientIdSet.Contains(await this._presenter.Internal_GetClientId()))
                {
                    await hubContext.Clients.Client(this._presenter.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                }

                foreach (var viewer in this._viewers)
                {
                    var viewerClientId = await viewer.Internal_GetClientId();
                    if (targetClientIdSet.Contains(viewerClientId))
                    {
                        await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).MessageReceived(this.GetPrimaryKeyString(), senderClientId, messageType, data);
                    }
                }
                break;
        }
    }
    public Task<bool> IsPresenter(string signalrConnectionId)
    {
        var isPresenter = this._presenter?.GetPrimaryKeyString() == signalrConnectionId;
        return Task.FromResult(isPresenter);
    }

    async Task IConnectionGrain.Internal_InitializePresenter(IClientGrain presenter)
    {
        if (this._presenter is not null)
            throw new InvalidOperationException("Presenter already initialized for this connection.");

        this._presenter = presenter;

        await hubContext.Clients
            .Client(presenter.GetPrimaryKeyString())
            .ConnectionStarted(this.GetPrimaryKeyString(), isPresenter: true);

        await this.NotifyConnectionChangedAsync();
    }
    async Task IConnectionGrain.Internal_AddViewer(IClientGrain viewer)
    {
        this._viewers.Add(viewer);

        await hubContext.Clients
            .Client(viewer.GetPrimaryKeyString())
            .ConnectionStarted(this.GetPrimaryKeyString(), isPresenter: false);

        await this.NotifyConnectionChangedAsync();
    }
    async Task IConnectionGrain.Internal_RemoveClient(IClientGrain client)
    {
        if (client.GetGrainId() == this._presenter?.GetGrainId())
        {
            foreach (var viewer in this._viewers)
            {
                await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).ConnectionStopped(this.GetPrimaryKeyString());
            }
            await hubContext.Clients.Client(client.GetPrimaryKeyString()).ConnectionStopped(this.GetPrimaryKeyString());

            this._presenter = null;
            this._viewers.Clear();

            this.DeactivateOnIdle();
        }
        else
        {
            this._viewers.RemoveWhere(f => f.GetGrainId() == client.GetGrainId());
            await hubContext.Clients.Client(client.GetPrimaryKeyString()).ConnectionStopped(this.GetPrimaryKeyString());

            await this.NotifyConnectionChangedAsync();
        }
    }
    async Task IConnectionGrain.Internal_DisplayNameChanged()
    {
        await this.NotifyConnectionChangedAsync();
    }

    private async Task NotifyConnectionChangedAsync()
    {
        this.EnsureInitialized();

        var presenterClientInfo = await this._presenter.Internal_GetClientInfo();
        var presenterInfo = new ClientInfo(presenterClientInfo.ClientId, presenterClientInfo.DisplayName);

        var viewerInfo = new List<ClientInfo>();
        foreach (var viewer in this._viewers)
        {
            var viewerClientInfo = await viewer.Internal_GetClientInfo();
            viewerInfo.Add(new ClientInfo(viewerClientInfo.ClientId, viewerClientInfo.DisplayName));
        }

        var connectionInfo = new ConnectionInfo(this.GetPrimaryKeyString(), presenterInfo, viewerInfo, this._properties);

        await hubContext.Clients.Client(this._presenter.GetPrimaryKeyString()).ConnectionChanged(connectionInfo);
        foreach (var viewer in this._viewers)
        {
            await hubContext.Clients.Client(viewer.GetPrimaryKeyString()).ConnectionChanged(connectionInfo);
        }
    }

    [MemberNotNull(nameof(_presenter))]
    private void EnsureInitialized()
    {
        if (this._presenter is null)
            throw new InvalidOperationException("Connection not initialized.");
    }

}

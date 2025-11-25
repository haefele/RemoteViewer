using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Services;

namespace RemoteViewer.Server.Hubs;

public interface IConnectionHubClient
{
    Task CredentialsAssigned(string clientId, string username, string password);
    Task StartPresenting(string connectionId);
    Task StartViewing(string connectionId);
}

public class ConnectionHub(IConnectionsService clientsService) : Hub<IConnectionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        await clientsService.Register(this.Context.ConnectionId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await clientsService.Unregister(this.Context.ConnectionId);
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        return await clientsService.TryConnectTo(this.Context.ConnectionId, username, password);
    }
}
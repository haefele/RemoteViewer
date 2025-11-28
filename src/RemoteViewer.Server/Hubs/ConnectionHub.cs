using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Services;

namespace RemoteViewer.Server.Hubs;

public interface IConnectionHubClient
{
    Task CredentialsAssigned(string clientId, string username, string password);

    Task ConnectionStarted(string connectionId, bool isPresenter);
    Task ConnectionChanged(ConnectionInfo connectionInfo);
    Task ConnectionStopped(string connectionId);

    Task MessageReceived(string connectionId, string senderClientId, string messageType, ReadOnlyMemory<byte> data);
}

public record ConnectionInfo(string ConnectionId, string PresenterClientId, List<string> ViewerClientIds);

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

    public async Task SendMessage(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination)
    {
        await clientsService.SendMessage(this.Context.ConnectionId, connectionId, messageType, data, destination);
    }
}
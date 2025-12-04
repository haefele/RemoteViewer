using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Services;
using RemoteViewer.Server.SharedAPI;
using ConnectionInfo = RemoteViewer.Server.SharedAPI.ConnectionInfo;

namespace RemoteViewer.Server.Hubs;

public interface IConnectionHubClient
{
    Task CredentialsAssigned(string clientId, string username, string password);

    Task ConnectionStarted(string connectionId, bool isPresenter);
    Task ConnectionChanged(ConnectionInfo connectionInfo);
    Task ConnectionStopped(string connectionId);

    Task MessageReceived(string connectionId, string senderClientId, string messageType, ReadOnlyMemory<byte> data);
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

    public async Task GenerateNewPassword()
    {
        await clientsService.GenerateNewPassword(this.Context.ConnectionId);
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        return await clientsService.TryConnectTo(this.Context.ConnectionId, username, password);
    }

    public async Task SendMessage(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null)
    {
        await clientsService.SendMessage(this.Context.ConnectionId, connectionId, messageType, data, destination, targetClientIds);
    }

    public async Task Disconnect(string connectionId)
    {
        await clientsService.DisconnectFromConnection(this.Context.ConnectionId, connectionId);
    }
}
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Orleans.Grains;
using RemoteViewer.Server.Services;
using RemoteViewer.Shared;
using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Hubs;

public interface IConnectionHubClient
{
    Task CredentialsAssigned(string clientId, string username, string password);

    Task ConnectionStarted(string connectionId, bool isPresenter);
    Task ConnectionChanged(ConnectionInfo connectionInfo);
    Task ConnectionStopped(string connectionId);

    Task MessageReceived(string connectionId, string senderClientId, string messageType, byte[] data);

}

[
    Authorize
]
public class ConnectionHub(
    IConnectionsService clientsService) : Hub<IConnectionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        var clientGuid = this.Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(clientGuid))
        {
            this.Context.Abort();
            return;
        }

        var displayName = this.Context.User?.FindFirstValue("display_name");
        await clientsService.Register(this.Context.ConnectionId, clientGuid, displayName);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await clientsService.Unregister(this.Context.ConnectionId);
    }

    public async Task GenerateNewPassword() => await clientsService.GenerateNewPassword(this.Context.ConnectionId);

    public async Task SetDisplayName(string displayName) => await clientsService.SetDisplayName(this.Context.ConnectionId, displayName);

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        return await clientsService.TryConnectTo(this.Context.ConnectionId, username, password);
    }

    public async Task SendMessage(string connectionId, string messageType, byte[] data, MessageDestination destination, List<string>? targetClientIds = null)
    {
        await clientsService.SendMessage(this.Context.ConnectionId, connectionId, messageType, data, destination, targetClientIds);
    }

    public async Task AckFrame(string connectionId)
    {
        await clientsService.AckFrame(this.Context.ConnectionId, connectionId);
    }


    public async Task SetConnectionProperties(string connectionId, ConnectionProperties properties)
    {
        await clientsService.SetConnectionProperties(this.Context.ConnectionId, connectionId, properties);
    }

    public async Task Disconnect(string connectionId)
    {
        await clientsService.DisconnectFromConnection(this.Context.ConnectionId, connectionId);
    }
}

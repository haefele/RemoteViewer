using Microsoft.AspNetCore.SignalR;
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

    Task VersionMismatch(string serverVersion, string clientVersion);

    Task IpcTokenValidated(string? connectionId);
}

public class ConnectionHub(IConnectionsService clientsService, IIpcTokenService ipcTokenService, ILogger<ConnectionHub> logger) : Hub<IConnectionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = this.Context.GetHttpContext();

        // IPC token validation request - no version check needed
        var ipcToken = httpContext?.Request.Headers["X-Ipc-Token"].ToString();
        if (string.IsNullOrEmpty(ipcToken) is false)
        {
            var connectionId = ipcTokenService.ValidateAndConsumeToken(ipcToken);
            await this.Clients.Caller.IpcTokenValidated(connectionId);
            this.Context.Abort();
            return;
        }

        // Normal client connection - check version
        var clientVersion = httpContext?.Request.Headers["X-Client-Version"].ToString();
        var serverVersion = ThisAssembly.AssemblyInformationalVersion;

        if (string.IsNullOrEmpty(clientVersion) || string.Equals(clientVersion, serverVersion, StringComparison.OrdinalIgnoreCase) is false)
        {
            logger.VersionMismatch(clientVersion, serverVersion, this.Context.ConnectionId);
            await this.Clients.Caller.VersionMismatch(serverVersion, clientVersion ?? "unknown");

            this.Context.Abort();
        }
        else
        {
            var displayName = httpContext?.Request.Headers["X-Display-Name"].ToString();
            await clientsService.Register(this.Context.ConnectionId, displayName);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await clientsService.Unregister(this.Context.ConnectionId);
    }

    public async Task GenerateNewPassword()
    {
        await clientsService.GenerateNewPassword(this.Context.ConnectionId);
    }

    public async Task SetDisplayName(string displayName)
    {
        await clientsService.SetDisplayName(this.Context.ConnectionId, displayName);
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        return await clientsService.TryConnectTo(this.Context.ConnectionId, username, password);
    }

    public async Task SendMessage(string connectionId, string messageType, byte[] data, MessageDestination destination, List<string>? targetClientIds = null)
    {
        await clientsService.SendMessage(this.Context.ConnectionId, connectionId, messageType, data, destination, targetClientIds);
    }

    public Task AckFrame(string connectionId)
    {
        return clientsService.AckFrame(this.Context.ConnectionId, connectionId);
    }


    public async Task SetConnectionProperties(string connectionId, ConnectionProperties properties)
    {
        await clientsService.SetConnectionProperties(this.Context.ConnectionId, connectionId, properties);
    }

    public async Task Disconnect(string connectionId)
    {
        await clientsService.DisconnectFromConnection(this.Context.ConnectionId, connectionId);
    }

    public async Task<string?> GenerateIpcAuthToken(string connectionId)
    {
        // Only the presenter of a connection can generate IPC tokens
        if (await clientsService.IsPresenterOfConnection(this.Context.ConnectionId, connectionId) is false)
            return null;

        return ipcTokenService.GenerateToken(connectionId);
    }
}

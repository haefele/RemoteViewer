using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Services;

namespace RemoteViewer.Server.Hubs;

public interface IConnectionHubClient
{
    Task ClientIdAssigned(string id, string password);
    Task StartPresenting(string connectionId);
}

public class ConnectionHub(IClientsService clientsService, IConnectionsService connectionsService) : Hub<IConnectionHubClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var client = clientsService.Register(this.Context.ConnectionId);
        this.Context.Client = client;

        await this.Clients.Caller.ClientIdAssigned(client.Id, client.Password);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        connectionsService.Remove(this.Context.Client);
        this.Context.Client.Dispose();

        await base.OnDisconnectedAsync(exception);
    }

    public async Task<string?> ConnectTo(string id, string password)
    {
        var destinationClient = clientsService.CheckPassword(id, password);

        if (destinationClient is null)
            return null;

        var connection = connectionsService.ConnectTo(this.Context.Client, destinationClient);
        await this.Clients.Client(destinationClient.SignalrConnectionId).StartPresenting(connection.Id);

        return connection.Id;
    }
}

public static class ContextExtensions
{
    extension(HubCallerContext context)
    {
        public IClient Client
        {
            get => (IClient?)context.Items["Client"] ?? throw new InvalidOperationException("Client not found");
            set => context.Items["Client"] = value;
        }
    }
}
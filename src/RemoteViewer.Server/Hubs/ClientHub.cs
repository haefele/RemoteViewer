using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Services;

namespace RemoteViewer.Server.Hubs;

public class ClientHub(IClientIdGenerator clientIdGenerator) : Hub
{
    public const string ClientIdItemKey = "ClientId";

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var clientId = clientIdGenerator.Generate();
        this.Context.Items[ClientIdItemKey] = clientId.Id;

        await this.Clients.Caller.SendAsync("ClientIdAssigned", clientId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);

        if (this.Context.Items.TryGetValue(ClientIdItemKey, out var c) && c is string clientId)
        {
            clientIdGenerator.Free(clientId);
        }
    }
}

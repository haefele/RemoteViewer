using Microsoft.AspNetCore.SignalR;

namespace RemoteViewer.Server.Common;

public class BatchedHubActions<THub, TClient>(IHubContext<THub, TClient> hubContext)
    where THub : Hub<TClient>
    where TClient : class
{
    private readonly List<Func<IHubClients<TClient>, Task>> _actions = new();

    public void Add(Func<IHubClients<TClient>, Task> action)
    {
        this._actions.Add(action);
    }

    public async Task ExecuteAll()
    {
        foreach (var action in this._actions)
        {
            await action(hubContext.Clients);
        }

        this._actions.Clear();
    }
}

public static class BatchedActionsExtensions
{
    extension<THub, TClient>(IHubContext<THub, TClient> self)
        where THub : Hub<TClient>
        where TClient : class
    {
        public BatchedHubActions<THub, TClient> BatchedActions()
        {
            return new BatchedHubActions<THub, TClient>(self);
        }
    }
}
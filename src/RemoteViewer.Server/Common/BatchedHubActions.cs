using Microsoft.AspNetCore.SignalR;

namespace RemoteViewer.Server.Common;

public class BatchedHubActions<THub, TClient>(IHubContext<THub, TClient> hubContext, ILogger logger)
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
            try
            {
                await action(hubContext.Clients);
            }
            catch (Exception ex)
            {
                // Client may have disconnected - log and continue
                logger.LogDebug(ex, "Failed to execute hub action, client may have disconnected");
            }
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
        public BatchedHubActions<THub, TClient> BatchedActions(ILogger logger)
        {
            return new BatchedHubActions<THub, TClient>(self, logger);
        }
    }
}
using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Common;
using RemoteViewer.Server.Hubs;
using System.Collections.ObjectModel;

namespace RemoteViewer.Server.Services;

public interface IConnectionsService
{
    Task Register(string signalrConnectionId);
    Task Unregister(string signalrConnectionId);

    Task<TryConnectError?> TryConnectTo(string connectionId, string username, string password);
}
public enum TryConnectError
{
    ViewerNotFound,
    IncorrectUsernameOrPassword,
}

public class ConnectionsService(IHubContext<ConnectionHub, IConnectionHubClient> connectionHub) : IConnectionsService, IDisposable
{
    private readonly List<Client> _clients = new();
    private readonly List<Connection> _connections = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public async Task Register(string signalrConnectionId)
    {
        var actions = connectionHub.BatchedActions();

        const string IdChars = "0123456789";
        const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";

        while (true)
        {
            string username = Random.Shared.GetString(IdChars, 10);
            string password = Random.Shared.GetString(PasswordChars, 8);

            using (this._lock.WriteLock())
            {
                if (this._clients.Any(c => c.Credentials.Username == username))
                    continue;

                var credentials = new Credentials(username, password);

                var client = new Client(Guid.NewGuid().ToString(), credentials, signalrConnectionId);
                this._clients.Add(client);

                actions.Add(f => f.Client(signalrConnectionId).CredentialsAssigned(client.Id, client.Credentials.Username, client.Credentials.Password));
                break;
            }
        }

        await actions.ExecuteAll();
    }

    public async Task Unregister(string signalrConnectionId)
    {
        using (this._lock.WriteLock())
        {
            foreach (var connection in this._connections)
            {
                connection.ClientDisconnected(signalrConnectionId);
            }

            this._connections.RemoveAll(f => f.Presenter.SignalrConnectionId == signalrConnectionId);
            this._clients.RemoveAll(c => c.SignalrConnectionId == signalrConnectionId);

            // TODO: Notify clients about disconnections
        }
    }

    public async Task<TryConnectError?> TryConnectTo(string connectionId, string username, string password)
    {
        var actions = connectionHub.BatchedActions();

        using (this._lock.UpgradeableReadLock())
        {
            var viewer = this._clients.FirstOrDefault(c => c.SignalrConnectionId == connectionId);
            if (viewer is null)
                return TryConnectError.ViewerNotFound;

            var presenter = this._clients.FirstOrDefault(c => c.Credentials.Username == username && string.Equals(c.Credentials.Password, password, StringComparison.OrdinalIgnoreCase));
            if (presenter is null)
                return TryConnectError.IncorrectUsernameOrPassword;

            using (this._lock.WriteLock())
            {
                var connection = this._connections.FirstOrDefault(c => c.Presenter == presenter);
                if (connection is null)
                {
                    connection = new Connection(Guid.NewGuid().ToString(), presenter);
                    this._connections.Add(connection);
                
                    actions.Add(f => f.Client(presenter.SignalrConnectionId).StartPresenting(connection.Id));
                }

                connection.AddViewer(viewer, actions);
            }
        }

        await actions.ExecuteAll();

        return null;
    }

    public void Dispose()
    {
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Internal
    private class Client(string id, Credentials credentials, string signalrConnectionId)
    {
        public string Id { get; } = id;
        public Credentials Credentials { get; } = credentials;
        public string SignalrConnectionId { get; } = signalrConnectionId;
    }
    private record class Credentials(string Username, string Password);

    private class Connection(string id, Client presenter)
    {
        public string Id { get; } = id;
        public Client Presenter { get; } = presenter;

        private readonly HashSet<Client> _viewers = new();
        public ReadOnlySet<Client> Viewers => _viewers.AsReadOnly();

        public void AddViewer(Client viewer, BatchedHubActions<ConnectionHub, IConnectionHubClient> actions)
        {
            this._viewers.Add(viewer);
            actions.Add(f => f.Client(viewer.SignalrConnectionId).StartViewing(this.Id));
        }

        public void ClientDisconnected(string signalrConnectionId)
        {
            this._viewers.RemoveWhere(v => v.SignalrConnectionId == signalrConnectionId);
            // TODO: Notify presenter about viewer disconnection
        }
    }
    #endregion
}
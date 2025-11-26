using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Common;
using RemoteViewer.Server.Hubs;
using System.Collections.ObjectModel;

using ConnectionHubBatchedActions = RemoteViewer.Server.Common.BatchedHubActions<RemoteViewer.Server.Hubs.ConnectionHub, RemoteViewer.Server.Hubs.IConnectionHubClient>;

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

                var client = Client.Create(Guid.NewGuid().ToString(), credentials, signalrConnectionId, actions);
                this._clients.Add(client);

                break;
            }
        }

        await actions.ExecuteAll();
    }

    public async Task Unregister(string signalrConnectionId)
    {
        var actions = connectionHub.BatchedActions();

        using (this._lock.WriteLock())
        {
            for (int i = this._connections.Count - 1; i >= 0; i--)
            {
                var connection = this._connections[i];

                var connectionStopped = connection.ClientDisconnected(signalrConnectionId, actions);
                if (connectionStopped)
                {
                    this._connections.Remove(connection);
                }
            }

            this._clients.RemoveAll(c => c.SignalrConnectionId == signalrConnectionId);
        }

        await actions.ExecuteAll();
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
                    connection = Connection.Create(Guid.NewGuid().ToString(), presenter, actions);
                    this._connections.Add(connection);
                }

                connection.AddViewer(viewer, actions);
            }
        }

        await actions.ExecuteAll();

        return null;
    }

    public void Dispose()
    {
        this._lock.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Internal
    private sealed class Client
    {
        public static Client Create(string id, Credentials credentials, string signalrConnectionId, ConnectionHubBatchedActions actions)
        {
            var client = new Client(id, credentials, signalrConnectionId);
            actions.Add(f => f.Client(signalrConnectionId).CredentialsAssigned(client.Id, client.Credentials.Username, client.Credentials.Password));

            return client;
        }
        private Client(string id, Credentials credentials, string signalrConnectionId)
        {
            this.Id = id;
            this.Credentials = credentials;
            this.SignalrConnectionId = signalrConnectionId;
        }

        public string Id { get; }
        public Credentials Credentials { get; }
        public string SignalrConnectionId { get; }
    }
    private sealed record class Credentials(string Username, string Password);

    private sealed class Connection
    {
        public static Connection Create(string id, Client presenter, ConnectionHubBatchedActions actions)
        {
            var connection = new Connection(id, presenter);
            actions.Add(f => f.Client(presenter.SignalrConnectionId).ConnectionStarted(connection.Id, isPresenter: true));

            return connection;
        }
        private Connection(string id, Client presenter)
        {
            this.Id = id;
            this.Presenter = presenter;
        }

        public string Id { get; }
        public Client Presenter { get; }

        private readonly HashSet<Client> _viewers = new();
        public ReadOnlySet<Client> Viewers => _viewers.AsReadOnly();

        public void AddViewer(Client viewer, ConnectionHubBatchedActions actions)
        {
            if (this._viewers.Add(viewer))
            {
                actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionStarted(this.Id, isPresenter: false));
                this.ConnectionChanged(actions);
            }
        }

        public bool ClientDisconnected(string signalrConnectionId, ConnectionHubBatchedActions actions)
        {
            // The presenter disconnected, let everyone know that the connection is closed now
            if (this.Presenter.SignalrConnectionId == signalrConnectionId)
            {
                foreach (var viewer in this._viewers)
                {
                    actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionStopped(this.Id));
                }

                return true;
            }
            // One of the viewers disconnected, let everyone know that the connection changed
            else
            {
                if (this._viewers.RemoveWhere(v => v.SignalrConnectionId == signalrConnectionId) > 0)
                    this.ConnectionChanged(actions);

                return false;
            }
        }

        private void ConnectionChanged(ConnectionHubBatchedActions actions)
        {
            var connectionInfo = new Hubs.ConnectionInfo(
                this.Id,
                this.Presenter.Id,
                this._viewers.Select(v => v.Id).ToList()
            );

            actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).ConnectionChanged(connectionInfo));
            
            foreach (var viewer in this._viewers)
            {
                actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionChanged(connectionInfo));
            }
        }
    }
    #endregion
}
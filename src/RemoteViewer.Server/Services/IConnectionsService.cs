using Microsoft.AspNetCore.SignalR;
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
    private readonly SemaphoreSlim _clientsLock = new(1, 1);
    private readonly List<Client> _clients = new();

    private readonly SemaphoreSlim _connectionsLock = new(1, 1);
    private readonly List<Connection> _connections = new();

    public async Task Register(string signalrConnectionId)
    {
        const string IdChars = "0123456789";
        const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";

        while (true)
        {
            string username = Random.Shared.GetString(IdChars, 10);
            string password = Random.Shared.GetString(PasswordChars, 8);

            Client client;

            await this._clientsLock.WaitAsync();
            try
            {
                if (this._clients.Any(c => c.Credentials.Username == username))
                    continue;

                var credentials = new Credentials(username, password);

                client = new Client(Guid.NewGuid().ToString(), credentials, signalrConnectionId);
                this._clients.Add(client);
            }
            finally
            {
                this._clientsLock.Release();
            }

            await connectionHub.Clients.Client(signalrConnectionId).CredentialsAssigned(client.Id, client.Credentials.Username, client.Credentials.Password);
            break;
        }
    }

    public async Task Unregister(string signalrConnectionId)
    {
        await this._clientsLock.WaitAsync();
        try
        {
            this._clients.RemoveAll(c => c.SignalrConnectionId == signalrConnectionId);
        }
        finally
        {
            this._clientsLock.Release();
        }

        await this._connectionsLock.WaitAsync();
        try
        {
            this._connections.RemoveAll(f => f.Presenter.SignalrConnectionId == signalrConnectionId);

            foreach (var connection in this._connections)
            {
                connection.ClientDisconnected(signalrConnectionId);
            }
        }
        finally
        {
            this._connectionsLock.Release();
        }
    }

    public async Task<TryConnectError?> TryConnectTo(string connectionId, string username, string password)
    {
        await this._clientsLock.WaitAsync();
        try
        {
            var viewer = this._clients.FirstOrDefault(c => c.SignalrConnectionId == connectionId);
            if (viewer is null)
                return TryConnectError.ViewerNotFound;

            var presenter = this._clients.FirstOrDefault(c => c.Credentials.Username == username && string.Equals(c.Credentials.Password, password, StringComparison.OrdinalIgnoreCase));
            if (presenter is null)
                return TryConnectError.IncorrectUsernameOrPassword;

            await this._connectionsLock.WaitAsync();
            try
            {
                var connection = this._connections.FirstOrDefault(c => c.Presenter == presenter);
                if (connection is null)
                {
                    connection = new Connection(Guid.NewGuid().ToString(), presenter);
                    this._connections.Add(connection);
                }

                connection.AddViewer(viewer);

                return null;
            }
            finally
            {
                this._connectionsLock.Release();
            }
        }
        finally
        {
            this._clientsLock.Release();
        }
    }

    public void Dispose()
    {
        _clientsLock.Dispose();
        _connectionsLock.Dispose();
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

        public void AddViewer(Client viewer)
        {
            this._viewers.Add(viewer);
        }

        public void ClientDisconnected(string signalrConnectionId)
        {
            this._viewers.RemoveWhere(v => v.SignalrConnectionId == signalrConnectionId);
        }
    }
    #endregion
}
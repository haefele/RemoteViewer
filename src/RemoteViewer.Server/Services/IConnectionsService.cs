using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Hubs;
using System.Collections.ObjectModel;

namespace RemoteViewer.Server.Services;

public interface IConnectionsService
{
    Task Register(string signalrConnectionId);
    void Unregister(string signalrConnectionId);

    Task<TryConnectError?> TryConnectTo(string connectionId, string username, string password);
}
public record class Client(string Id, Credentials Credentials, string SignalrConnectionId);
public record class Credentials(string Username, string Password);
public enum TryConnectError
{
    ViewerNotFound,
    IncorrectUsernameOrPassword,
}

public class Connection(string id, Client presenter)
{
    public string Id { get; } = id;
    public Client Presenter { get; } = presenter;

    private readonly List<Client> _viewers = new();
    public ReadOnlyCollection<Client> Viewers => _viewers.AsReadOnly();

    public void AddViewer(Client viewer)
    {
        this._viewers.Add(viewer);
    }

    public void ClientDisconnected(string signalrConnectionId)
    {
        this._viewers.RemoveAll(v => v.SignalrConnectionId == signalrConnectionId);
    }
}

public class ConnectionsService(IHubContext<ConnectionHub, IConnectionHubClient> connectionHub) : IConnectionsService
{
    private readonly Lock _clientsLock = new();
    private readonly List<Client> _clients = new();

    public async Task Register(string signalrConnectionId)
    {
        const string IdChars = "0123456789";
        const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";

        while (true)
        {
            string username = Random.Shared.GetString(IdChars, 10);
            string password = Random.Shared.GetString(PasswordChars, 8);

            Client client;

            using (this._clientsLock.EnterScope())
            {
                if (this._clients.Any(c => c.Credentials.Username == username))
                    continue;

                var credentials = new Credentials(username, password);

                client = new Client(Guid.NewGuid().ToString(), credentials, signalrConnectionId);
                this._clients.Add(client);
            }

            await connectionHub.Clients.Client(signalrConnectionId).CredentialsAssigned(client.Id, client.Credentials.Username, client.Credentials.Password);
        }
    }

    public void Unregister(string signalrConnectionId)
    {
        using (this._clientsLock.EnterScope())
        {
            this._clients.RemoveAll(c => c.SignalrConnectionId == signalrConnectionId);
        }
    }

    public async Task<TryConnectError?> TryConnectTo(string connectionId, string username, string password)
    {
        using (this._clientsLock.EnterScope())
        {
            var viewer = this._clients.FirstOrDefault(c => c.SignalrConnectionId == connectionId);
            if (viewer is null)
                return TryConnectError.ViewerNotFound;

            var presenter = this._clients.FirstOrDefault(c => c.Credentials.Username == username && string.Equals(c.Credentials.Password, password, StringComparison.OrdinalIgnoreCase));
            if (presenter is null)
                return TryConnectError.IncorrectUsernameOrPassword;

            throw new NotImplementedException();
        }
    }
}
namespace RemoteViewer.Server.Services;

public interface IClientsService
{
    IClient Register(string signalrConnectionId);
    IClient? CheckPassword(string id, string password);
}

public interface IClient : IDisposable
{
    string Id { get; }
    string Password { get; }

    string SignalrConnectionId { get; }
}

public class ClientsService : IClientsService
{
    private readonly Lock _clientsLock = new();
    private readonly List<Client> _clients = new();

    private const string IdChars = "0123456789";
    private const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";

    public IClient Register(string signalrConnectionId)
    {
        while (true)
        {
            string id = Random.Shared.GetString(IdChars, 10);
            string password = Random.Shared.GetString(PasswordChars, 8);

            using (this._clientsLock.EnterScope())
            {
                if (this._clients.Any(c => c.Id == id))
                    continue;

                var client = new Client(this, id, password, signalrConnectionId);
                this._clients.Add(client);

                return client;
            }
        }
    }

    public IClient? CheckPassword(string id, string password)
    {
        using (this._clientsLock.EnterScope())
        {
            return this._clients.FirstOrDefault(c => c.Id == id && string.Equals(c.Password, password, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void Release(Client client)
    {
        using (this._clientsLock.EnterScope())
        {
            this._clients.Remove(client);
        }
    }

    private class Client(ClientsService owner, string id, string password, string signalrConnectionId) : IClient
    {
        public string Id { get } = id;

        public string Password { get; } = password;

        public string SignalrConnectionId {  get; } = signalrConnectionId;

        public void Dispose()
        {
            owner.Release(this);
        }
    }
}
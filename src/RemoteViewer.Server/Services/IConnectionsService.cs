namespace RemoteViewer.Server.Services;

public interface IConnectionsService
{
    Connection ConnectTo(IClient viewer, IClient presenter);
    void Remove(IClient client);
}

public class ConnectionService : IConnectionsService
{
    private readonly Lock _connectionsLock = new();
    private readonly List<Connection> _connections = new();
    
    public Connection ConnectTo(IClient viewer, IClient presenter)
    {
        using (this._connectionsLock.EnterScope())
        {
            var existingConnection = this._connections.FirstOrDefault(c => c.Presenter.Id == presenter.Id);
            if (existingConnection is null)
            {
                existingConnection = new Connection(Guid.NewGuid().ToString(), presenter);
                this._connections.Add(existingConnection);
            }

            existingConnection.AddViewer(viewer);

            return existingConnection;
        }
    }

    public void Remove(IClient client)
    {
        using (this._connectionsLock.EnterScope())
        {
            this._connections.RemoveAll(c => c.Presenter == client);

            foreach (var connection in this._connections)
            {
                connection.Viewers.RemoveAll(v => v == client);
            }
        }
    }
}

public class Connection(string id, IClient presenter)
{
    public string Id { get; } = id;
    public IClient Presenter { get; } = presenter;
    public List<IClient> Viewers { get; } = new();
    public void AddViewer(IClient viewer)
    {
        this.Viewers.Add(viewer);
    }
}
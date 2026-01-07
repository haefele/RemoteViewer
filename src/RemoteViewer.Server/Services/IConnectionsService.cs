using Microsoft.AspNetCore.SignalR;
using RemoteViewer.Server.Common;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared;
using System.Collections.ObjectModel;
using System.Text;

using ConnectionHubBatchedActions = RemoteViewer.Server.Common.BatchedHubActions<RemoteViewer.Server.Hubs.ConnectionHub, RemoteViewer.Server.Hubs.IConnectionHubClient>;
using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Services;

public interface IConnectionsService
{
    Task Register(string signalrConnectionId, string? displayName);
    Task Unregister(string signalrConnectionId);
    Task GenerateNewPassword(string signalrConnectionId);
    Task SetDisplayName(string signalrConnectionId, string displayName);

    Task<TryConnectError?> TryConnectTo(string signalrConnectionId, string username, string password);
    Task DisconnectFromConnection(string signalrConnectionId, string connectionId);
    Task SetConnectionProperties(string signalrConnectionId, string connectionId, ConnectionProperties properties);
    Task SendMessage(string signalrConnectionId, string connectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null);

    Task<bool> IsPresenterOfConnection(string signalrConnectionId, string connectionId);
}


public class ConnectionsService(IHubContext<ConnectionHub, IConnectionHubClient> connectionHub, ILoggerFactory loggerFactory) : IConnectionsService, IDisposable
{
    private readonly ILogger<ConnectionsService> _logger = loggerFactory.CreateLogger<ConnectionsService>();
    private readonly List<Client> _clients = new();
    private readonly List<Connection> _connections = new();
    private readonly ReaderWriterLockSlim _lock = new();

    public async Task Register(string signalrConnectionId, string? displayName)
    {
        this._logger.ClientRegistrationStarted(signalrConnectionId);

        var actions = connectionHub.BatchedActions(this._logger);

        const string IdChars = "0123456789";

        var attempts = 0;
        while (true)
        {
            attempts++;

            var username = Random.Shared.GetString(IdChars, 10);
            var password = this.GetPassword();

            using (this._lock.WriteLock())
            {
                if (this._clients.Any(c => c.Credentials.Username == username))
                {
                    this._logger.UsernameCollision(attempts);
                    continue;
                }

                var credentials = new Credentials(username, password);

                var client = Client.Create(Guid.NewGuid().ToString(), credentials, signalrConnectionId, actions);
                this._clients.Add(client);

                if (!string.IsNullOrEmpty(displayName))
                {
                    client.SetDisplayName(displayName);
                }

                this._logger.ClientRegistered(client.Id, client.Credentials.Username, this._clients.Count);

                break;
            }
        }

        if (attempts > 1)
        {
            this._logger.MultipleRegistrationAttempts(attempts);
        }

        await actions.ExecuteAll();
    }

    public async Task Unregister(string signalrConnectionId)
    {
        this._logger.ClientUnregistrationStarted(signalrConnectionId);

        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.WriteLock())
        {
            var connectionsRemoved = 0;

            for (var i = this._connections.Count - 1; i >= 0; i--)
            {
                var connection = this._connections[i];

                var connectionStopped = connection.ClientDisconnected(signalrConnectionId, actions);
                if (connectionStopped)
                {
                    this._connections.Remove(connection);
                    connectionsRemoved++;

                    this._logger.ConnectionStoppedPresenterDisconnect(connection.Id);
                }
            }

            var clientsRemoved = this._clients.RemoveAll(c => c.SignalrConnectionId == signalrConnectionId);

            this._logger.ClientUnregistered(signalrConnectionId, clientsRemoved, connectionsRemoved, this._clients.Count, this._connections.Count);
        }

        await actions.ExecuteAll();
    }

    public async Task GenerateNewPassword(string signalrConnectionId)
    {
        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.WriteLock())
        {
            var client = this._clients.FirstOrDefault(c => c.SignalrConnectionId == signalrConnectionId);
            if (client is null)
                return;

            var newPassword = this.GetPassword();
            client.UpdatePassword(newPassword, actions);

            this._logger.ClientPasswordChanged(client.Id);
        }

        await actions.ExecuteAll();
    }

    private string GetPassword()
    {
        const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return Random.Shared.GetString(PasswordChars, 8);
    }

    public async Task SetDisplayName(string signalrConnectionId, string displayName)
    {
        this._logger.ClientDisplayNameChangeStarted(signalrConnectionId, displayName);

        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.WriteLock())
        {
            var client = this._clients.FirstOrDefault(c => c.SignalrConnectionId == signalrConnectionId);
            if (client is null)
                return;

            client.SetDisplayName(displayName);
            this._logger.ClientDisplayNameChanged(client.Id, displayName);

            // Broadcast ConnectionChanged to any connections this client is part of
            foreach (var connection in this._connections)
            {
                if (connection.Presenter.SignalrConnectionId == signalrConnectionId ||
                    connection.Viewers.Any(v => v.SignalrConnectionId == signalrConnectionId))
                {
                    connection.ConnectionChanged(actions);
                }
            }
        }

        await actions.ExecuteAll();
    }

    private static string FormatUsername(string username)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < username.Length; i++)
        {
            if (i > 0 && (username.Length - i) % 3 == 0)
                sb.Append(' ');
            sb.Append(username[i]);
        }
        return sb.ToString();
    }

    private static string StripSpaces(string value)
    {
        return value.Replace(" ", "");
    }

    public async Task<TryConnectError?> TryConnectTo(string signalrConnectionId, string username, string password)
    {
        this._logger.ConnectionAttemptStarted(signalrConnectionId, username);

        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.UpgradeableReadLock())
        {
            var viewer = this._clients.FirstOrDefault(c => c.SignalrConnectionId == signalrConnectionId);
            if (viewer is null)
            {
                this._logger.ViewerNotFound(signalrConnectionId);
                return TryConnectError.ViewerNotFound;
            }

            var presenter = this._clients.FirstOrDefault(c => c.Credentials.Username == StripSpaces(username) && string.Equals(c.Credentials.Password, password, StringComparison.OrdinalIgnoreCase));
            if (presenter is null)
            {
                this._logger.IncorrectCredentials(viewer.Id, username);
                return TryConnectError.IncorrectUsernameOrPassword;
            }

            if (viewer == presenter)
            {
                this._logger.CannotConnectToYourself(viewer.Id);
                return TryConnectError.CannotConnectToYourself;
            }

            using (this._lock.WriteLock())
            {
                var connection = this._connections.FirstOrDefault(c => c.Presenter == presenter);
                if (connection is null)
                {
                    connection = Connection.Create(Guid.NewGuid().ToString(), presenter, actions, loggerFactory);
                    this._connections.Add(connection);
                }

                connection.AddViewer(viewer, actions);
            }
        }

        await actions.ExecuteAll();

        this._logger.ConnectionAttemptSucceeded(username);

        return null;
    }

    public async Task DisconnectFromConnection(string signalrConnectionId, string connectionId)
    {
        this._logger.DisconnectFromConnectionStarted(signalrConnectionId, connectionId);

        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.WriteLock())
        {
            var connection = this._connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection is null)
            {
                this._logger.DisconnectConnectionNotFound(connectionId);
                return;
            }

            var connectionStopped = connection.ClientDisconnected(signalrConnectionId, actions);
            if (connectionStopped)
            {
                this._connections.Remove(connection);
            }
        }

        await actions.ExecuteAll();
    }

    public async Task SetConnectionProperties(string signalrConnectionId, string connectionId, ConnectionProperties properties)
    {
        var actions = connectionHub.BatchedActions(this._logger);

        using (this._lock.WriteLock())
        {
            var sender = this._clients.FirstOrDefault(c => c.SignalrConnectionId == signalrConnectionId);
            if (sender is null)
            {
                this._logger.MessageSenderNotFound(signalrConnectionId);
                return;
            }

            var connection = this._connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection is null)
            {
                this._logger.MessageConnectionNotFound(connectionId);
                return;
            }

            if (connection.Presenter != sender)
            {
                this._logger.MessageSenderNotInConnection(sender.Id, connectionId);
                return;
            }

            connection.UpdateProperties(properties, actions);
        }

        await actions.ExecuteAll();
    }

    public async Task SendMessage(string signalrConnectionId, string connectionId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null)
    {
        var actions = connectionHub.BatchedActions(this._logger);
        string? senderId = null;

        using (this._lock.ReadLock())
        {
            var sender = this._clients.FirstOrDefault(c => c.SignalrConnectionId == signalrConnectionId);
            if (sender is null)
            {
                this._logger.MessageSenderNotFound(signalrConnectionId);
                return;
            }

            senderId = sender.Id;
            this._logger.MessageSendStarted(senderId, connectionId, messageType, destination, data.Length);

            var connection = this._connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection is null)
            {
                this._logger.MessageConnectionNotFound(connectionId);
                return;
            }

            if (connection.SendMessage(sender, messageType, data, destination, targetClientIds, actions) is false)
            {
                this._logger.MessageSenderNotInConnection(senderId, connectionId);
                return;
            }
        }

        await actions.ExecuteAll();
        this._logger.MessageSendCompleted(senderId, connectionId, messageType);
    }

    public Task<bool> IsPresenterOfConnection(string signalrConnectionId, string connectionId)
    {
        using (this._lock.ReadLock())
        {
            var connection = this._connections.FirstOrDefault(c => c.Id == connectionId);
            if (connection is null)
                return Task.FromResult(false);

            return Task.FromResult(connection.Presenter.SignalrConnectionId == signalrConnectionId);
        }
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
            actions.Add(f => f.Client(signalrConnectionId).CredentialsAssigned(client.Id, FormatUsername(client.Credentials.Username), client.Credentials.Password));

            return client;
        }

        private Client(string id, Credentials credentials, string signalrConnectionId)
        {
            this.Id = id;
            this.Credentials = credentials;
            this.SignalrConnectionId = signalrConnectionId;
        }

        public string Id { get; }
        public Credentials Credentials { get; private set; }
        public string SignalrConnectionId { get; }
        public string DisplayName { get; private set; } = string.Empty;

        public void SetDisplayName(string displayName) => this.DisplayName = displayName;

        public void UpdatePassword(string newPassword, ConnectionHubBatchedActions actions)
        {
            this.Credentials = this.Credentials with { Password = newPassword };
            actions.Add(f => f.Client(this.SignalrConnectionId).CredentialsAssigned(this.Id, FormatUsername(this.Credentials.Username), this.Credentials.Password));
        }
    }
    private sealed record class Credentials(string Username, string Password);

    private sealed class Connection
    {
        public static Connection Create(string id, Client presenter, ConnectionHubBatchedActions actions, ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger($"{typeof(ConnectionsService).FullName}.Connection[{id}]");

            var connection = new Connection(id, presenter, logger);
            actions.Add(f => f.Client(presenter.SignalrConnectionId).ConnectionStarted(connection.Id, isPresenter: true));

            return connection;
        }

        private Connection(string id, Client presenter, ILogger logger)
        {
            this.Id = id;
            this.Presenter = presenter;
            this._logger = logger;
            this.Properties = new ConnectionProperties(CanSendSecureAttentionSequence: false, InputBlockedViewerIds: [], AvailableDisplays: []);

            this._logger.ConnectionCreated(id, presenter.Id);
        }

        private readonly ILogger _logger;

        public string Id { get; }
        public Client Presenter { get; }
        public ConnectionProperties Properties { get; private set; }

        private readonly HashSet<Client> _viewers = new();
        public ReadOnlySet<Client> Viewers => this._viewers.AsReadOnly();

        public void AddViewer(Client viewer, ConnectionHubBatchedActions actions)
        {
            if (this._viewers.Add(viewer))
            {
                actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionStarted(this.Id, isPresenter: false));
                this._logger.ViewerAdded(this.Id, viewer.Id, this._viewers.Count);
                this.ConnectionChanged(actions);
            }
            else
            {
                this._logger.ViewerAlreadyPresent(this.Id, viewer.Id);
            }
        }

        public bool ClientDisconnected(string signalrConnectionId, ConnectionHubBatchedActions actions)
        {
            if (this.Presenter.SignalrConnectionId == signalrConnectionId)
            {
                this._logger.PresenterDisconnected(this.Id, this.Presenter.Id, this._viewers.Count);

                foreach (var viewer in this._viewers)
                {
                    actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionStopped(this.Id));
                }

                actions.Add(f => f.Client(signalrConnectionId).ConnectionStopped(this.Id));

                return true;
            }
            else
            {
                var removedCount = this._viewers.RemoveWhere(v => v.SignalrConnectionId == signalrConnectionId);
                if (removedCount > 0)
                {
                    this._logger.ViewerDisconnected(this.Id, removedCount, this._viewers.Count);
                    this.Properties = this.NormalizeProperties(this.Properties);
                    this.ConnectionChanged(actions);

                    actions.Add(f => f.Client(signalrConnectionId).ConnectionStopped(this.Id));
                }

                return false;
            }
        }

        public void UpdateProperties(ConnectionProperties properties, ConnectionHubBatchedActions actions)
        {
            this.Properties = this.NormalizeProperties(properties);
            this.ConnectionChanged(actions);
        }

        public bool SendMessage(Client sender, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds, ConnectionHubBatchedActions actions)
        {
            var isSenderPresenter = this.Presenter == sender;
            var isSenderViewer = this._viewers.Contains(sender);

            if (isSenderPresenter is false && isSenderViewer is false)
                return false;

            switch (destination)
            {
                case MessageDestination.PresenterOnly:
                    if (isSenderViewer)
                    {
                        actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                        this._logger.MessageSentToPresenter(this.Id, sender.Id, this.Presenter.Id);
                    }
                    break;

                case MessageDestination.AllViewers:
                    foreach (var viewer in this._viewers)
                    {
                        actions.Add(f => f.Client(viewer.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                    }
                    this._logger.MessageSentToViewers(this.Id, sender.Id, this._viewers.Count);
                    break;

                case MessageDestination.All:
                    actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));

                    foreach (var viewer in this._viewers)
                    {
                        actions.Add(f => f.Client(viewer.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                    }
                    this._logger.MessageSentToAll(this.Id, sender.Id, this._viewers.Count + 1);
                    break;

                case MessageDestination.AllExceptSender:
                    var recipientCount = 0;
                    if (this.Presenter != sender)
                    {
                        actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                        recipientCount++;
                    }

                    foreach (var viewer in this._viewers)
                    {
                        if (viewer != sender)
                        {
                            actions.Add(f => f.Client(viewer.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                            recipientCount++;
                        }
                    }
                    this._logger.MessageSentToAll(this.Id, sender.Id, recipientCount);
                    break;

                case MessageDestination.SpecificClients:
                    if (targetClientIds is null || targetClientIds.Count == 0)
                        break;

                    var sentCount = 0;

                    // Check if presenter is in target list
                    if (targetClientIds.Contains(this.Presenter.Id))
                    {
                        actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                        sentCount++;
                    }

                    // Check viewers
                    foreach (var viewer in this._viewers)
                    {
                        if (targetClientIds.Contains(viewer.Id))
                        {
                            actions.Add(f => f.Client(viewer.SignalrConnectionId).MessageReceived(this.Id, sender.Id, messageType, data));
                            sentCount++;
                        }
                    }

                    this._logger.MessageSentToSpecificClients(this.Id, sender.Id, sentCount, targetClientIds.Count);
                    break;
            }

            return true;
        }

        private ConnectionProperties NormalizeProperties(ConnectionProperties properties)
        {
            var viewerIds = this._viewers.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            var blockedIds = properties.InputBlockedViewerIds
                .Where(id => viewerIds.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return properties with { InputBlockedViewerIds = blockedIds };
        }

        public void ConnectionChanged(ConnectionHubBatchedActions actions)
        {
            var connectionInfo = new ConnectionInfo(
                this.Id,
                new ClientInfo(this.Presenter.Id, this.Presenter.DisplayName),
                this._viewers.Select(v => new ClientInfo(v.Id, v.DisplayName)).ToList(),
                this.Properties
            );

            this._logger.ConnectionStateChange(this.Id, this._viewers.Count);

            actions.Add(f => f.Client(this.Presenter.SignalrConnectionId).ConnectionChanged(connectionInfo));

            foreach (var viewer in this._viewers)
            {
                actions.Add(f => f.Client(viewer.SignalrConnectionId).ConnectionChanged(connectionInfo));
            }
        }
    }
    #endregion
}

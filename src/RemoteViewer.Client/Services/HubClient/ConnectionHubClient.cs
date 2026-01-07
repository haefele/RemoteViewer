using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nerdbank.MessagePack.SignalR;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.HubClient;

public class ConnectionHubClient : IAsyncDisposable
{
    private readonly ILogger<ConnectionHubClient> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly HubConnection _connection;
    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private bool _disposed;

    public ConnectionHubClient(
        ILogger<ConnectionHubClient> logger,
        IServiceProvider serviceProvider,
        IOptions<ConnectionHubClientOptions> options)
    {
        this._logger = logger;
        this._serviceProvider = serviceProvider;

        this._connection = new HubConnectionBuilder()
            .WithUrl($"{options.Value.BaseUrl}/connection", httpOptions =>
            {
                httpOptions.Headers.Add("X-Client-Version", ThisAssembly.AssemblyInformationalVersion);
                httpOptions.Headers.Add("X-Display-Name", this.DisplayName);
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider)
            .Build();

        this._connection.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
        {
            this.ClientId = clientId;
            this.Username = username;
            this.Password = password;

            this._logger.LogInformation("Credentials assigned - ClientId: {ClientId}, Username: {Username}", clientId, username);
            this._credentialsAssigned?.Invoke(this, new CredentialsAssignedEventArgs(clientId, username, password));
        });

        this._connection.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
        {
            var connection = ActivatorUtilities.CreateInstance<Connection>(
                this._serviceProvider,
                this,
                connectionId,
                isPresenter);

            this._connections[connectionId] = connection;

            this._logger.LogInformation("Connection started - ConnectionId: {ConnectionId}, IsPresenter: {IsPresenter}", connectionId, isPresenter);
            this.ConnectionStarted?.Invoke(this, new ConnectionStartedEventArgs(connection));
        });

        this._connection.On<ConnectionInfo>("ConnectionChanged", async (connectionInfo) =>
        {
            this._logger.LogInformation("Connection changed - ConnectionId: {ConnectionId}, PresenterClientId: {PresenterClientId}, ViewerCount: {ViewerCount}", connectionInfo.ConnectionId, connectionInfo.Presenter.ClientId, connectionInfo.Viewers.Count);

            var connection = await this.WaitForConnection(connectionInfo.ConnectionId);
            ((IConnectionImpl)connection).OnConnectionChanged(connectionInfo);
        });

        this._connection.On<string>("ConnectionStopped", async (connectionId) =>
        {
            this._logger.LogInformation("Connection stopped - ConnectionId: {ConnectionId}", connectionId);

            var connection = await this.WaitForConnection(connectionId);
            ((IConnectionImpl)connection).OnClosed();
        });

        this._connection.On<string, string, string, byte[]>("MessageReceived", async (connectionId, senderClientId, messageType, data) =>
        {
            this._logger.LogDebug("Message received - ConnectionId: {ConnectionId}, SenderClientId: {SenderClientId}, MessageType: {MessageType}, DataLength: {DataLength}", connectionId, senderClientId, messageType, data.Length);

            var connection = await this.WaitForConnection(connectionId);
            ((IConnectionImpl)connection).OnMessageReceived(senderClientId, messageType, data);
        });

        this._connection.On<string, string>("VersionMismatch", (serverVersion, clientVersion) =>
        {
            this.HasVersionMismatch = true;
            this.ServerVersion = serverVersion;

            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
        });

        this._connection.Closed += (error) =>
        {
            this.IsReconnecting = false;

            if (error is not null)
            {
                this._logger.LogWarning(error, "Connection closed with error - ClientId: {ClientId}, Username: {Username}", this.ClientId, this.Username);
            }
            else
            {
                this._logger.LogInformation("Connection closed - ClientId: {ClientId}, Username: {Username}", this.ClientId, this.Username);
            }

            // Clear stale credentials - server will assign new ones after reconnect
            this.ClientId = null;
            this.Username = null;
            this.Password = null;

            this.CloseAllConnections();
            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);

            if (this._disposed)
            {
                this._logger.LogDebug("Not reconnecting - client is disposed");
                return Task.CompletedTask;
            }

            if (this.HasVersionMismatch)
            {
                this._logger.LogWarning("Not reconnecting due to version mismatch");
                return Task.CompletedTask;
            }

            this._logger.LogInformation("Attempting to reconnect after connection closed");
            return this.ConnectToHub();
        };

        this._connection.Reconnecting += (error) =>
        {
            this.IsReconnecting = true;

            this._logger.LogWarning(error, "Connection reconnecting - ClientId: {ClientId}, Username: {Username}", this.ClientId, this.Username);

            // Clear stale credentials - server will assign new ones after reconnect
            this.ClientId = null;
            this.Username = null;
            this.Password = null;

            this.CloseAllConnections();
            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        };

        this._connection.Reconnected += (connectionId) =>
        {
            this.IsReconnecting = false;

            this._logger.LogInformation("Connection reconnected - NewConnectionId: {ConnectionId}", connectionId);
            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };
    }

    private async Task<Connection> WaitForConnection(string connectionId)
    {
        while (true)
        {
            if (this._connections.TryGetValue(connectionId, out var connection))
                return connection;

            await Task.Delay(50);
        }
    }

    private void CloseAllConnections()
    {
        foreach (var connection in this._connections.Values)
        {
            ((IConnectionImpl)connection).OnClosed();
        }
        this._connections.Clear();
    }

    public string? ClientId { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public string DisplayName { get; private set; } = Environment.UserName;

    public IReadOnlyCollection<Connection> Connections => this._connections.Values.ToList();

    public string? ServerVersion { get; private set; }
    public bool HasVersionMismatch { get; private set; }

    private EventHandler<CredentialsAssignedEventArgs>? _credentialsAssigned;
    public event EventHandler<CredentialsAssignedEventArgs>? CredentialsAssigned
    {
        add
        {
            this._credentialsAssigned += value;

            // Notify new subscriber of current state if credentials already assigned
            if (this.ClientId is not null)
            {
                value?.Invoke(this, new CredentialsAssignedEventArgs(this.ClientId, this.Username!, this.Password!));
            }
        }
        remove => this._credentialsAssigned -= value;
    }

    public event EventHandler<ConnectionStartedEventArgs>? ConnectionStarted;

    public bool IsConnected => this._connection.State == HubConnectionState.Connected;
    public bool IsReconnecting { get; private set; }

    private EventHandler? _hubConnectionStatusChanged;
    public event EventHandler? HubConnectionStatusChanged
    {
        add
        {
            this._hubConnectionStatusChanged += value;
            value?.Invoke(this, EventArgs.Empty);
        }

        remove => this._hubConnectionStatusChanged -= value;
    }

    public async Task ConnectToHub()
    {
        while (true)
        {
            try
            {
                this._logger.LogInformation("Connecting to server...");
                await this._connection.StartAsync();

                this._logger.LogInformation("Connected to server successfully");
                this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);

                return;
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Connection attempt failed, retrying in 200 milliseconds");
                await Task.Delay(200);
            }
        }
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        if (!this.IsConnected || this.IsReconnecting)
            return null;

        try
        {
            this._logger.LogInformation("Attempting to connect to device with username: {Username}", username);
            var error = await this._connection.InvokeAsync<TryConnectError?>("ConnectTo", username, password);

            if (error is null)
            {
                this._logger.LogInformation("Successfully connected to device with username: {Username}", username);
            }
            else
            {
                this._logger.LogWarning("Failed to connect to device with username: {Username}, Error: {Error}", username, error);
            }

            return error;
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to connect to device - hub disconnected");
            return null;
        }
    }

    public async Task GenerateNewPassword()
    {
        if (!this.IsConnected || this.IsReconnecting)
            return;

        try
        {
            this._logger.LogInformation("Generating new password");
            await this._connection.InvokeAsync("GenerateNewPassword");
            this._logger.LogInformation("New password generated");
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to generate new password - hub disconnected");
        }
    }

    public async Task SetDisplayName(string displayName)
    {
        this.DisplayName = displayName;

        if (!this.IsConnected || this.IsReconnecting)
            return;

        try
        {
            this._logger.LogInformation("Setting display name to: {DisplayName}", displayName);
            await this._connection.InvokeAsync("SetDisplayName", displayName);
            this._logger.LogInformation("Display name set successfully");
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to set display name - hub disconnected");
        }
    }

    public async ValueTask DisposeAsync()
    {
        this._disposed = true;
        this._logger.LogDebug("Disposing HubClient");
        await this._connection.DisposeAsync();
    }

    internal async Task SendMessageAsync(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination, List<string>? targetClientIds = null)
    {
        if (!this.IsConnected || this.IsReconnecting)
            return;

        try
        {
            this._logger.LogDebug("Sending message - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, Destination: {Destination}", connectionId, messageType, data.Length, destination);
            await this._connection.SendAsync("SendMessage", connectionId, messageType, data, destination, targetClientIds);
            this._logger.LogDebug("Message sent successfully");
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to send message - hub disconnected");
        }
    }

    internal async Task DisconnectAsync(string connectionId)
    {
        if (!this.IsConnected || this.IsReconnecting)
            return;

        try
        {
            this._logger.LogInformation("Disconnecting from connection: {ConnectionId}", connectionId);
            await this._connection.InvokeAsync("Disconnect", connectionId);
            this._logger.LogInformation("Disconnected from connection: {ConnectionId}", connectionId);
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to disconnect - hub disconnected");
        }
    }

    internal async Task SetConnectionPropertiesAsync(string connectionId, ConnectionProperties properties)
    {
        if (!this.IsConnected || this.IsReconnecting)
            return;

        try
        {
            this._logger.LogDebug("Setting connection properties - ConnectionId: {ConnectionId}", connectionId);
            await this._connection.InvokeAsync("SetConnectionProperties", connectionId, properties);
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to set connection properties - hub disconnected");
        }
    }

    internal async Task<string?> GenerateIpcAuthTokenAsync(string connectionId)
    {
        if (!this.IsConnected || this.IsReconnecting)
            return null;

        try
        {
            this._logger.LogDebug("Generating IPC auth token for connection: {ConnectionId}", connectionId);
            return await this._connection.InvokeAsync<string?>("GenerateIpcAuthToken", connectionId);
        }
        catch (Exception ex) when (!this.IsConnected)
        {
            this._logger.LogWarning(ex, "Failed to generate IPC auth token - hub disconnected");
            return null;
        }
    }
}

#region EventArgs Classes

public sealed class CredentialsAssignedEventArgs : EventArgs
{
    public CredentialsAssignedEventArgs(string clientId, string username, string password)
    {
        this.ClientId = clientId;
        this.Username = username;
        this.Password = password;
    }

    public string ClientId { get; }
    public string Username { get; }
    public string Password { get; }
}

public sealed class ConnectionStartedEventArgs : EventArgs
{
    public ConnectionStartedEventArgs(Connection connection)
    {
        this.Connection = connection;
    }

    public Connection Connection { get; }
}

#endregion

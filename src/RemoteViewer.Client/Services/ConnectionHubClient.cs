using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Nerdbank.MessagePack.SignalR;
using PolyType.ReflectionProvider;
using RemoteViewer.Server.SharedAPI;
using System.Collections.Concurrent;

namespace RemoteViewer.Client.Services;

public sealed class ConnectionHubClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly ILogger<ConnectionHubClient> _logger;

    public ConnectionHubClient(string serverUrl, ILogger<ConnectionHubClient> logger)
    {
        this._logger = logger;

        this._connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/connection")
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(ReflectionTypeShapeProvider.Default)
            .Build();

        this._connection.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
        {
            this.ClientId = clientId;
            this.Username = username;
            this.Password = password;

            this._logger.LogInformation("Credentials assigned - ClientId: {ClientId}, Username: {Username}", clientId, username);
            CredentialsAssigned?.Invoke(this, new CredentialsAssignedEventArgs(clientId, username, password));
        });

        this._connection.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
        {
            this._logger.LogInformation("Connection started - ConnectionId: {ConnectionId}, IsPresenter: {IsPresenter}", connectionId, isPresenter);
            ConnectionStarted?.Invoke(this, new ConnectionStartedEventArgs(connectionId, isPresenter));
        });

        this._connection.On<ConnectionInfo>("ConnectionChanged", (connectionInfo) =>
        {
            this._connections[connectionInfo.ConnectionId] = connectionInfo;
            this._logger.LogInformation("Connection changed - ConnectionId: {ConnectionId}, PresenterClientId: {PresenterClientId}, ViewerCount: {ViewerCount}",
                connectionInfo.ConnectionId, connectionInfo.PresenterClientId, connectionInfo.ViewerClientIds.Count);
            ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(connectionInfo));
        });

        this._connection.On<string>("ConnectionStopped", (connectionId) =>
        {
            this._connections.TryRemove(connectionId, out _);
            this._logger.LogInformation("Connection stopped - ConnectionId: {ConnectionId}", connectionId);
            ConnectionStopped?.Invoke(this, new ConnectionStoppedEventArgs(connectionId));
        });

        this._connection.On<string, string, string, ReadOnlyMemory<byte>>("MessageReceived", (connectionId, senderClientId, messageType, data) =>
        {
            this._logger.LogInformation("Message received - ConnectionId: {ConnectionId}, SenderClientId: {SenderClientId}, MessageType: {MessageType}, DataLength: {DataLength}",
                connectionId, senderClientId, messageType, data.Length);
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(connectionId, senderClientId, messageType, data));
        });

        this._connection.Closed += (error) =>
        {
            if (error is not null)
            {
                this._logger.LogWarning(error, "Connection closed with error");
            }
            else
            {
                this._logger.LogInformation("Connection closed");
            }

            Closed?.Invoke(this, new HubClosedEventArgs(error));

            // Reconnect to the server if we lost connection
            return this.StartAsync();
        };

        this._connection.Reconnecting += (error) =>
        {
            this._logger.LogWarning(error, "Connection reconnecting");
            Reconnecting?.Invoke(this, new HubReconnectingEventArgs(error));
            return Task.CompletedTask;
        };

        this._connection.Reconnected += (connectionId) =>
        {
            this._logger.LogInformation("Connection reconnected - ConnectionId: {ConnectionId}", connectionId);
            Reconnected?.Invoke(this, new HubReconnectedEventArgs(connectionId));
            return Task.CompletedTask;
        };
    }

    public string? ClientId { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public IReadOnlyDictionary<string, ConnectionInfo> Connections => this._connections;

    public event EventHandler<CredentialsAssignedEventArgs>? CredentialsAssigned;
    public event EventHandler<ConnectionStartedEventArgs>? ConnectionStarted;
    public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<ConnectionStoppedEventArgs>? ConnectionStopped;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

    public event EventHandler<HubClosedEventArgs>? Closed;
    public event EventHandler<HubReconnectingEventArgs>? Reconnecting;
    public event EventHandler<HubReconnectedEventArgs>? Reconnected;

    public async Task StartAsync()
    {
        while (true)
        {
            try
            {
                if (this._connection.State is HubConnectionState.Connected or HubConnectionState.Reconnecting)
                {
                    this._logger.LogInformation("Already connected to server");
                    return;
                }

                this._logger.LogInformation("Connecting to server...");
                await this._connection.StartAsync();
                this._logger.LogInformation("Connected to server successfully");

                return;
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Connection attempt failed, retrying in 500 milliseconds");
                await Task.Delay(500);
            }
        }
    }

    public async Task StopAsync()
    {
        this._logger.LogInformation("Disconnecting from server...");
        await this._connection.StopAsync();
        this._logger.LogInformation("Disconnected from server");
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
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

    public async Task SendMessage(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination, IReadOnlyList<string>? targetClientIds = null)
    {
        this._logger.LogDebug("Sending message - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, Destination: {Destination}",
            connectionId, messageType, data.Length, destination);
        await this._connection.InvokeAsync("SendMessage", connectionId, messageType, data, destination, targetClientIds);
        this._logger.LogDebug("Message sent successfully");
    }

    public async Task Disconnect(string connectionId)
    {
        this._logger.LogInformation("Disconnecting from connection: {ConnectionId}", connectionId);
        await this._connection.InvokeAsync("Disconnect", connectionId);
        this._logger.LogInformation("Disconnected from connection: {ConnectionId}", connectionId);
    }

    public async Task ReconnectAsync()
    {
        this._logger.LogInformation("Reconnecting to get fresh credentials...");
        await this._connection.StopAsync();
        await this.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        this._logger.LogDebug("Disposing HubClient");
        await this._connection.DisposeAsync();
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
    public ConnectionStartedEventArgs(string connectionId, bool isPresenter)
    {
        this.ConnectionId = connectionId;
        this.IsPresenter = isPresenter;
    }

    public string ConnectionId { get; }
    public bool IsPresenter { get; }
}

public sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(ConnectionInfo connectionInfo)
    {
        this.ConnectionInfo = connectionInfo;
    }

    public ConnectionInfo ConnectionInfo { get; }
}

public sealed class ConnectionStoppedEventArgs : EventArgs
{
    public ConnectionStoppedEventArgs(string connectionId)
    {
        this.ConnectionId = connectionId;
    }

    public string ConnectionId { get; }
}

public sealed class MessageReceivedEventArgs : EventArgs
{
    public MessageReceivedEventArgs(string connectionId, string senderClientId, string messageType, ReadOnlyMemory<byte> data)
    {
        this.ConnectionId = connectionId;
        this.SenderClientId = senderClientId;
        this.MessageType = messageType;
        this.Data = data;
    }

    public string ConnectionId { get; }
    public string SenderClientId { get; }
    public string MessageType { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

public sealed class HubClosedEventArgs : EventArgs
{
    public HubClosedEventArgs(Exception? error)
    {
        this.Error = error;
    }

    public Exception? Error { get; }
}

public sealed class HubReconnectingEventArgs : EventArgs
{
    public HubReconnectingEventArgs(Exception? error)
    {
        this.Error = error;
    }

    public Exception? Error { get; }
}

public sealed class HubReconnectedEventArgs : EventArgs
{
    public HubReconnectedEventArgs(string? connectionId)
    {
        this.ConnectionId = connectionId;
    }

    public string? ConnectionId { get; }
}

#endregion

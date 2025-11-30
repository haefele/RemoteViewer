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
        _logger = logger;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/connection")
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(ReflectionTypeShapeProvider.Default)
            .Build();

        _connection.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
        {
            ClientId = clientId;
            Username = username;
            Password = password;
            _logger.LogInformation("Credentials assigned - ClientId: {ClientId}, Username: {Username}", clientId, username);
            CredentialsAssigned?.Invoke(this, new CredentialsAssignedEventArgs(clientId, username, password));
        });

        _connection.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
        {
            _logger.LogInformation("Connection started - ConnectionId: {ConnectionId}, IsPresenter: {IsPresenter}", connectionId, isPresenter);
            ConnectionStarted?.Invoke(this, new ConnectionStartedEventArgs(connectionId, isPresenter));
        });

        _connection.On<ConnectionInfo>("ConnectionChanged", (connectionInfo) =>
        {
            _connections[connectionInfo.ConnectionId] = connectionInfo;
            _logger.LogInformation("Connection changed - ConnectionId: {ConnectionId}, PresenterClientId: {PresenterClientId}, ViewerCount: {ViewerCount}",
                connectionInfo.ConnectionId, connectionInfo.PresenterClientId, connectionInfo.ViewerClientIds.Count);
            ConnectionChanged?.Invoke(this, new ConnectionChangedEventArgs(connectionInfo));
        });

        _connection.On<string>("ConnectionStopped", (connectionId) =>
        {
            _connections.TryRemove(connectionId, out _);
            _logger.LogInformation("Connection stopped - ConnectionId: {ConnectionId}", connectionId);
            ConnectionStopped?.Invoke(this, new ConnectionStoppedEventArgs(connectionId));
        });

        _connection.On<string, string, string, ReadOnlyMemory<byte>>("MessageReceived", (connectionId, senderClientId, messageType, data) =>
        {
            _logger.LogInformation("Message received - ConnectionId: {ConnectionId}, SenderClientId: {SenderClientId}, MessageType: {MessageType}, DataLength: {DataLength}",
                connectionId, senderClientId, messageType, data.Length);
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(connectionId, senderClientId, messageType, data));
        });

        _connection.Closed += (error) =>
        {
            if (error is not null)
            {
                _logger.LogWarning(error, "Connection closed with error");
            }
            else
            {
                _logger.LogInformation("Connection closed");
            }

            Closed?.Invoke(this, new ConnectionClosedEventArgs(error));

            // Reconnect to the server if we lost connection
            return StartAsync();
        };

        _connection.Reconnecting += (error) =>
        {
            _logger.LogWarning(error, "Connection reconnecting");
            Reconnecting?.Invoke(this, new ReconnectingEventArgs(error));
            return Task.CompletedTask;
        };

        _connection.Reconnected += (connectionId) => 
        {
            _logger.LogInformation("Connection reconnected - ConnectionId: {ConnectionId}", connectionId);
            Reconnected?.Invoke(this, new ReconnectedEventArgs(connectionId));
            return Task.CompletedTask;
        };
    }

    public string? ClientId { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }
    public IReadOnlyDictionary<string, ConnectionInfo> Connections => _connections;

    public event EventHandler<CredentialsAssignedEventArgs>? CredentialsAssigned;
    public event EventHandler<ConnectionStartedEventArgs>? ConnectionStarted;
    public event EventHandler<ConnectionChangedEventArgs>? ConnectionChanged;
    public event EventHandler<ConnectionStoppedEventArgs>? ConnectionStopped;
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ConnectionClosedEventArgs>? Closed;
    public event EventHandler<ReconnectingEventArgs>? Reconnecting;
    public event EventHandler<ReconnectedEventArgs>? Reconnected;

    public async Task StartAsync()
    {
        while (true)
        {
            try
            {
                _logger.LogInformation("Connecting to server...");
                await _connection.StartAsync();
                _logger.LogInformation("Connected to server successfully");

                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Connection attempt failed, retrying in 500 milliseconds");
                await Task.Delay(500);
            }
        }
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("Disconnecting from server...");
        await _connection.StopAsync();
        _logger.LogInformation("Disconnected from server");
    }

    public async Task<TryConnectError?> ConnectTo(string username, string password)
    {
        _logger.LogInformation("Attempting to connect to device with username: {Username}", username);
        var error = await _connection.InvokeAsync<TryConnectError?>("ConnectTo", username, password);

        if (error is null)
        {
            _logger.LogInformation("Successfully connected to device with username: {Username}", username);
        }
        else
        {
            _logger.LogWarning("Failed to connect to device with username: {Username}, Error: {Error}", username, error);
        }

        return error;
    }

    public async Task SendMessage(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination)
    {
        _logger.LogDebug("Sending message - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, Destination: {Destination}",
            connectionId, messageType, data.Length, destination);
        await _connection.InvokeAsync("SendMessage", connectionId, messageType, data, destination);
        _logger.LogDebug("Message sent successfully");
    }

    public async Task SendMessageToViewers(string connectionId, string messageType, ReadOnlyMemory<byte> data, IReadOnlyList<string> targetViewerClientIds)
    {
        _logger.LogDebug("Sending message to viewers - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, TargetCount: {TargetCount}",
            connectionId, messageType, data.Length, targetViewerClientIds.Count);
        await _connection.InvokeAsync("SendMessageToViewers", connectionId, messageType, data, targetViewerClientIds);
        _logger.LogDebug("Message sent to viewers successfully");
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("Disposing HubClient");
        await _connection.DisposeAsync();
    }
}

#region EventArgs Classes

public sealed class CredentialsAssignedEventArgs : EventArgs
{
    public CredentialsAssignedEventArgs(string clientId, string username, string password)
    {
        ClientId = clientId;
        Username = username;
        Password = password;
    }

    public string ClientId { get; }
    public string Username { get; }
    public string Password { get; }
}

public sealed class ConnectionStartedEventArgs : EventArgs
{
    public ConnectionStartedEventArgs(string connectionId, bool isPresenter)
    {
        ConnectionId = connectionId;
        IsPresenter = isPresenter;
    }

    public string ConnectionId { get; }
    public bool IsPresenter { get; }
}

public sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(ConnectionInfo connectionInfo)
    {
        ConnectionInfo = connectionInfo;
    }

    public ConnectionInfo ConnectionInfo { get; }
}

public sealed class ConnectionStoppedEventArgs : EventArgs
{
    public ConnectionStoppedEventArgs(string connectionId)
    {
        ConnectionId = connectionId;
    }

    public string ConnectionId { get; }
}

public sealed class MessageReceivedEventArgs : EventArgs
{
    public MessageReceivedEventArgs(string connectionId, string senderClientId, string messageType, ReadOnlyMemory<byte> data)
    {
        ConnectionId = connectionId;
        SenderClientId = senderClientId;
        MessageType = messageType;
        Data = data;
    }

    public string ConnectionId { get; }
    public string SenderClientId { get; }
    public string MessageType { get; }
    public ReadOnlyMemory<byte> Data { get; }
}

public sealed class ConnectionClosedEventArgs : EventArgs
{
    public ConnectionClosedEventArgs(Exception? error)
    {
        Error = error;
    }

    public Exception? Error { get; }
}

public sealed class ReconnectingEventArgs : EventArgs
{
    public ReconnectingEventArgs(Exception? error)
    {
        Error = error;
    }

    public Exception? Error { get; }
}

public sealed class ReconnectedEventArgs : EventArgs
{
    public ReconnectedEventArgs(string? connectionId)
    {
        ConnectionId = connectionId;
    }

    public string? ConnectionId { get; }
}

#endregion

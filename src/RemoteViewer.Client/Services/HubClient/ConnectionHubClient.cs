using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Nerdbank.MessagePack.SignalR;
using PolyType.ReflectionProvider;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class ConnectionHubClient : IAsyncDisposable
{
    private readonly ILogger<ConnectionHubClient> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDisplayService _displayService;
    private readonly IScreenshotService _screenshotService;
    private readonly HubConnection _connection;
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

#if DEBUG
    private static readonly string s_baseUrl = "http://localhost:8080";
#else
    private static readonly string s_baseUrl = "https://rdp.xemio.net";
#endif

    public ConnectionHubClient(ILogger<ConnectionHubClient> logger, ILoggerFactory loggerFactory, IDisplayService displayService, IScreenshotService screenshotService)
    {
        this._logger = logger;
        this._loggerFactory = loggerFactory;
        this._displayService = displayService;
        this._screenshotService = screenshotService;

        this._connection = new HubConnectionBuilder()
            .WithUrl($"{s_baseUrl}/connection", options =>
            {
                options.Headers.Add("X-Client-Version", ThisAssembly.AssemblyInformationalVersion);
            })
            .WithAutomaticReconnect()
            .AddMessagePackProtocol(ReflectionTypeShapeProvider.Default)
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
            var connection = new Connection(
                connectionId,
                isPresenter,
                sendMessageAsync: (messageType, data, destination, targetClientIds) => this.SendMessage(connectionId, messageType, data, destination, targetClientIds),
                disconnectAsync: () => this.Disconnect(connectionId),
                this._loggerFactory.CreateLogger<Connection>(),
                displayService: isPresenter ? this._displayService : null,
                screenshotService: isPresenter ? this._screenshotService : null);

            this._connections[connectionId] = connection;

            this._logger.LogInformation("Connection started - ConnectionId: {ConnectionId}, IsPresenter: {IsPresenter}", connectionId, isPresenter);
            this.ConnectionStarted?.Invoke(this, new ConnectionStartedEventArgs(connection));
        });

        this._connection.On<ConnectionInfo>("ConnectionChanged", async (connectionInfo) =>
        {
            this._logger.LogInformation("Connection changed - ConnectionId: {ConnectionId}, PresenterClientId: {PresenterClientId}, ViewerCount: {ViewerCount}", connectionInfo.ConnectionId, connectionInfo.PresenterClientId, connectionInfo.ViewerClientIds.Count);

            var connection = await this.WaitForConnection(connectionInfo.ConnectionId);
            connection.OnViewersChanged(connectionInfo.ViewerClientIds);
        });

        this._connection.On<string>("ConnectionStopped", async (connectionId) =>
        {
            this._logger.LogInformation("Connection stopped - ConnectionId: {ConnectionId}", connectionId);

            var connection = await this.WaitForConnection(connectionId);
            connection.OnClosed();
        });

        this._connection.On<string, string, string, byte[]>("MessageReceived", async (connectionId, senderClientId, messageType, data) =>
        {
            this._logger.LogDebug("Message received - ConnectionId: {ConnectionId}, SenderClientId: {SenderClientId}, MessageType: {MessageType}, DataLength: {DataLength}", connectionId, senderClientId, messageType, data.Length);

            var connection = await this.WaitForConnection(connectionId);
            connection.OnMessageReceived(senderClientId, messageType, data);
        });

        this._connection.On<string, string>("VersionMismatch", (serverVersion, clientVersion) =>
        {
            this.HasVersionMismatch = true;
            this.ServerVersion = serverVersion;

            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);
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

            this.CloseAllConnections();
            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);

            if (this.HasVersionMismatch)
            {
                this._logger.LogWarning("Not reconnecting due to version mismatch");
                return Task.CompletedTask;
            }

            return this.ConnectToHub();
        };

        this._connection.Reconnecting += (error) =>
        {
            this._logger.LogWarning(error, "Connection reconnecting");
            this.CloseAllConnections();
            this._hubConnectionStatusChanged?.Invoke(this, EventArgs.Empty);

            return Task.CompletedTask;
        };

        this._connection.Reconnected += (connectionId) =>
        {
            this._logger.LogInformation("Connection reconnected - ConnectionId: {ConnectionId}", connectionId);
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
            connection.OnClosed();
        }
        this._connections.Clear();
    }

    public string? ClientId { get; private set; }
    public string? Username { get; private set; }
    public string? Password { get; private set; }

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
        this._logger.LogDebug("Sending message - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, Destination: {Destination}", connectionId, messageType, data.Length, destination);
        await this._connection.SendAsync("SendMessage", connectionId, messageType, data, destination, targetClientIds);
        this._logger.LogDebug("Message sent successfully");
    }

    public async Task Disconnect(string connectionId)
    {
        this._logger.LogInformation("Disconnecting from connection: {ConnectionId}", connectionId);
        await this._connection.InvokeAsync("Disconnect", connectionId);
        this._logger.LogInformation("Disconnected from connection: {ConnectionId}", connectionId);
    }

    public async Task GenerateNewPassword()
    {
        this._logger.LogInformation("Generating new password");
        await this._connection.InvokeAsync("GenerateNewPassword");
        this._logger.LogInformation("New password generated");
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
    public ConnectionStartedEventArgs(Connection connection)
    {
        this.Connection = connection;
    }

    public Connection Connection { get; }
}

#endregion

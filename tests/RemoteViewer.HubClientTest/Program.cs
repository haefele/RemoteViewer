using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Nerdbank.MessagePack.SignalR;
using PolyType.ReflectionProvider;
using RemoteViewer.Server.SharedAPI;
using System.Collections.Concurrent;

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});

var logger = loggerFactory.CreateLogger<Program>();

Console.WriteLine("RemoteViewer Hub Client Test");
Console.WriteLine("=============================");
Console.WriteLine();

var serverUrl = "http://localhost:5000";
Console.Write($"Server URL (default: {serverUrl}): ");
var input = Console.ReadLine();
if (!string.IsNullOrWhiteSpace(input))
{
    serverUrl = input;
}

var client = new ConnectionHubClient(serverUrl, loggerFactory.CreateLogger<ConnectionHubClient>());

client.CredentialsAssigned += (sender, e) =>
{
    Console.WriteLine($"[SERVER] Credentials assigned:");
    Console.WriteLine($"  Client ID: {e.ClientId}");
    Console.WriteLine($"  Username: {e.Username}");
    Console.WriteLine($"  Password: {e.Password}");
};

client.ConnectionStarted += (sender, e) =>
{
    Console.WriteLine($"[SERVER] Connection started:");
    Console.WriteLine($"  Connection ID: {e.ConnectionId}");
    Console.WriteLine($"  Role: {(e.IsPresenter ? "Presenter" : "Viewer")}");
};

client.ConnectionChanged += (sender, e) =>
{
    Console.WriteLine($"[SERVER] Connection changed:");
    Console.WriteLine($"  Connection ID: {e.ConnectionInfo.ConnectionId}");
    Console.WriteLine($"  Presenter Client ID: {e.ConnectionInfo.PresenterClientId}");
    Console.WriteLine($"  Viewer Client IDs: {string.Join(", ", e.ConnectionInfo.ViewerClientIds)}");
};

client.ConnectionStopped += (sender, e) =>
{
    Console.WriteLine($"[SERVER] Connection stopped:");
    Console.WriteLine($"  Connection ID: {e.ConnectionId}");
};

client.MessageReceived += (sender, e) =>
{
    Console.WriteLine($"[SERVER] Message received:");
    Console.WriteLine($"  Connection ID: {e.ConnectionId}");
    Console.WriteLine($"  From Client ID: {e.SenderClientId}");
    Console.WriteLine($"  Message Type: {e.MessageType}");
    Console.WriteLine($"  Data Length: {e.Data.Length} bytes");
    Console.WriteLine($"  Data: {System.Text.Encoding.UTF8.GetString(e.Data.Span)}");
};

client.Closed += (sender, e) =>
{
    Console.WriteLine($"[CONNECTION] Connection closed: {e.Error?.Message}");
};

client.Reconnecting += (sender, e) =>
{
    Console.WriteLine($"[CONNECTION] Reconnecting: {e.Error?.Message}");
};

client.Reconnected += (sender, e) =>
{
    Console.WriteLine($"[CONNECTION] Reconnected: {e.ConnectionId}");
};

await client.StartAsync();

while (true)
{
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  1. Connect to another device");
    Console.WriteLine("  2. Send message");
    Console.WriteLine("  3. Show current state");
    Console.WriteLine("  4. Exit");
    Console.Write("Choose command: ");

    var choice = Console.ReadLine();
    Console.WriteLine();

    switch (choice)
    {
        case "1":
            await ConnectToDevice(client);
            break;

        case "2":
            await SendTestMessage(client);
            break;

        case "3":
            ShowState(client);
            break;

        case "4":
            await client.StopAsync();
            return;

        default:
            Console.WriteLine("Invalid choice");
            break;
    }
}

static async Task ConnectToDevice(ConnectionHubClient client)
{
    Console.Write("Enter username: ");
    var username = Console.ReadLine();

    Console.Write("Enter password: ");
    var password = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
    {
        Console.WriteLine("Username and password are required");
        return;
    }

    Console.WriteLine($"Connecting to {username}...");

    var error = await client.ConnectTo(username, password);

    if (error is null)
    {
        Console.WriteLine("Connection successful!");
    }
    else
    {
        Console.WriteLine($"Connection failed: {error}");
    }
}

static async Task SendTestMessage(ConnectionHubClient client)
{
    if (client.Connections.Count == 0)
    {
        Console.WriteLine("No active connections. Connect to a device first.");
        return;
    }

    Console.WriteLine("Active connections:");
    var connectionsList = client.Connections.Values.ToList();
    for (var i = 0; i < connectionsList.Count; i++)
    {
        Console.WriteLine($"  {i + 1}. {connectionsList[i].ConnectionId}");
    }

    Console.Write("Select connection (number): ");
    if (!int.TryParse(Console.ReadLine(), out var index) || index < 1 || index > connectionsList.Count)
    {
        Console.WriteLine("Invalid selection");
        return;
    }

    var selectedConnection = connectionsList[index - 1];

    Console.WriteLine("Message destination:");
    Console.WriteLine("  1. Presenter only");
    Console.WriteLine("  2. All viewers");
    Console.WriteLine("  3. All");
    Console.Write("Select destination (number): ");

    if (!int.TryParse(Console.ReadLine(), out var destChoice) || destChoice < 1 || destChoice > 3)
    {
        Console.WriteLine("Invalid destination");
        return;
    }

    var destination = destChoice switch
    {
        1 => MessageDestination.PresenterOnly,
        2 => MessageDestination.AllViewers,
        3 => MessageDestination.All,
        _ => MessageDestination.All
    };

    Console.Write("Enter message type: ");
    var messageType = Console.ReadLine() ?? "test";

    Console.Write("Enter message content: ");
    var content = Console.ReadLine() ?? "Hello!";

    var data = System.Text.Encoding.UTF8.GetBytes(content);

    Console.WriteLine($"Sending message to connection {selectedConnection.ConnectionId}...");
    await client.SendMessage(selectedConnection.ConnectionId, messageType, data, destination);
    Console.WriteLine("Message sent!");
}

static void ShowState(ConnectionHubClient client)
{
    Console.WriteLine("Current State:");
    Console.WriteLine($"  Client ID: {client.ClientId ?? "Not assigned"}");
    Console.WriteLine($"  Username: {client.Username ?? "Not assigned"}");
    Console.WriteLine($"  Password: {client.Password ?? "Not assigned"}");
    Console.WriteLine($"  Active Connections: {client.Connections.Count}");

    foreach (var conn in client.Connections.Values)
    {
        Console.WriteLine($"    - Connection ID: {conn.ConnectionId}");
        Console.WriteLine($"      Presenter: {conn.PresenterClientId}");
        Console.WriteLine($"      Viewers: {string.Join(", ", conn.ViewerClientIds)}");
    }
}

#region EventArgs Classes

sealed class CredentialsAssignedEventArgs : EventArgs
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

sealed class ConnectionStartedEventArgs : EventArgs
{
    public ConnectionStartedEventArgs(string connectionId, bool isPresenter)
    {
        this.ConnectionId = connectionId;
        this.IsPresenter = isPresenter;
    }

    public string ConnectionId { get; }
    public bool IsPresenter { get; }
}

sealed class ConnectionChangedEventArgs : EventArgs
{
    public ConnectionChangedEventArgs(ConnectionInfo connectionInfo)
    {
        this.ConnectionInfo = connectionInfo;
    }

    public ConnectionInfo ConnectionInfo { get; }
}

sealed class ConnectionStoppedEventArgs : EventArgs
{
    public ConnectionStoppedEventArgs(string connectionId)
    {
        this.ConnectionId = connectionId;
    }

    public string ConnectionId { get; }
}

sealed class MessageReceivedEventArgs : EventArgs
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

sealed class ConnectionClosedEventArgs : EventArgs
{
    public ConnectionClosedEventArgs(Exception? error)
    {
        this.Error = error;
    }

    public Exception? Error { get; }
}

sealed class ReconnectingEventArgs : EventArgs
{
    public ReconnectingEventArgs(Exception? error)
    {
        this.Error = error;
    }

    public Exception? Error { get; }
}

sealed class ReconnectedEventArgs : EventArgs
{
    public ReconnectedEventArgs(string? connectionId)
    {
        this.ConnectionId = connectionId;
    }

    public string? ConnectionId { get; }
}

#endregion

sealed class ConnectionHubClient : IAsyncDisposable
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

            Closed?.Invoke(this, new ConnectionClosedEventArgs(error));

            // Reconnect to the server if we lost connection
            return this.StartAsync();
        };

        this._connection.Reconnecting += (error) =>
        {
            this._logger.LogWarning(error, "Connection reconnecting");
            Reconnecting?.Invoke(this, new ReconnectingEventArgs(error));
            return Task.CompletedTask;
        };

        this._connection.Reconnected += (connectionId) =>
        {
            this._logger.LogInformation("Connection reconnected - ConnectionId: {ConnectionId}", connectionId);
            Reconnected?.Invoke(this, new ReconnectedEventArgs(connectionId));
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
    public event EventHandler<ConnectionClosedEventArgs>? Closed;
    public event EventHandler<ReconnectingEventArgs>? Reconnecting;
    public event EventHandler<ReconnectedEventArgs>? Reconnected;

    public async Task StartAsync()
    {
        while (true)
        {
            try
            {
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

    public async Task SendMessage(string connectionId, string messageType, ReadOnlyMemory<byte> data, MessageDestination destination)
    {
        this._logger.LogDebug("Sending message - ConnectionId: {ConnectionId}, MessageType: {MessageType}, DataLength: {DataLength}, Destination: {Destination}",
            connectionId, messageType, data.Length, destination);
        await this._connection.InvokeAsync("SendMessage", connectionId, messageType, data, destination);
        this._logger.LogDebug("Message sent successfully");
    }

    public async ValueTask DisposeAsync()
    {
        this._logger.LogDebug("Disposing HubClient");
        await this._connection.DisposeAsync();
    }
}

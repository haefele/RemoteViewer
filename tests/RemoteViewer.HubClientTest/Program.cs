using Microsoft.AspNetCore.SignalR.Client;

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

var client = new HubClient(serverUrl);
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
            await client.ConnectToDevice();
            break;
            
        case "2":
            await client.SendTestMessage();
            break;
            
        case "3":
            client.ShowState();
            break;
            
        case "4":
            await client.StopAsync();
            return;
            
        default:
            Console.WriteLine("Invalid choice");
            break;
    }
}

sealed class HubClient
{
    private readonly HubConnection _connection;
    private string? _clientId;
    private string? _username;
    private string? _password;
    private readonly Dictionary<string, ConnectionInfo> _connections = new();
    
    public HubClient(string serverUrl)
    {
        _connection = new HubConnectionBuilder()
            .WithUrl($"{serverUrl}/connection")
            .WithAutomaticReconnect()
            .Build();
        
        _connection.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
        {
            _clientId = clientId;
            _username = username;
            _password = password;
            Console.WriteLine($"[SERVER] Credentials assigned:");
            Console.WriteLine($"  Client ID: {clientId}");
            Console.WriteLine($"  Username: {username}");
            Console.WriteLine($"  Password: {password}");
        });
        
        _connection.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
        {
            Console.WriteLine($"[SERVER] Connection started:");
            Console.WriteLine($"  Connection ID: {connectionId}");
            Console.WriteLine($"  Role: {(isPresenter ? "Presenter" : "Viewer")}");
        });
        
        _connection.On<ConnectionInfo>("ConnectionChanged", (connectionInfo) =>
        {
            _connections[connectionInfo.ConnectionId] = connectionInfo;
            Console.WriteLine($"[SERVER] Connection changed:");
            Console.WriteLine($"  Connection ID: {connectionInfo.ConnectionId}");
            Console.WriteLine($"  Presenter Client ID: {connectionInfo.PresenterClientId}");
            Console.WriteLine($"  Viewer Client IDs: {string.Join(", ", connectionInfo.ViewerClientIds)}");
        });
        
        _connection.On<string>("ConnectionStopped", (connectionId) =>
        {
            _connections.Remove(connectionId);
            Console.WriteLine($"[SERVER] Connection stopped:");
            Console.WriteLine($"  Connection ID: {connectionId}");
        });
        
        _connection.On<string, string, ReadOnlyMemory<byte>>("MessageReceived", (senderClientId, messageType, data) =>
        {
            Console.WriteLine($"[SERVER] Message received:");
            Console.WriteLine($"  From Client ID: {senderClientId}");
            Console.WriteLine($"  Message Type: {messageType}");
            Console.WriteLine($"  Data Length: {data.Length} bytes");
            Console.WriteLine($"  Data: {System.Text.Encoding.UTF8.GetString(data.Span)}");
        });
        
        _connection.Closed += async (error) =>
        {
            Console.WriteLine($"[CONNECTION] Connection closed: {error?.Message}");
            await Task.CompletedTask;
        };
        
        _connection.Reconnecting += async (error) =>
        {
            Console.WriteLine($"[CONNECTION] Reconnecting: {error?.Message}");
            await Task.CompletedTask;
        };
        
        _connection.Reconnected += async (connectionId) =>
        {
            Console.WriteLine($"[CONNECTION] Reconnected: {connectionId}");
            await Task.CompletedTask;
        };
    }
    
    public async Task StartAsync()
    {
        Console.WriteLine("Connecting to server...");
        await _connection.StartAsync();
        Console.WriteLine("Connected!");
    }
    
    public async Task StopAsync()
    {
        Console.WriteLine("Disconnecting from server...");
        await _connection.StopAsync();
        Console.WriteLine("Disconnected!");
    }
    
    public async Task ConnectToDevice()
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
        
        var error = await _connection.InvokeAsync<TryConnectError?>("ConnectTo", username, password);
        
        if (error is null)
        {
            Console.WriteLine("Connection successful!");
        }
        else
        {
            Console.WriteLine($"Connection failed: {error}");
        }
    }
    
    public async Task SendTestMessage()
    {
        if (_connections.Count == 0)
        {
            Console.WriteLine("No active connections. Connect to a device first.");
            return;
        }
        
        Console.WriteLine("Active connections:");
        var connectionsList = _connections.Values.ToList();
        for (int i = 0; i < connectionsList.Count; i++)
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
        await _connection.InvokeAsync("SendMessage", selectedConnection.ConnectionId, messageType, data, destination);
        Console.WriteLine("Message sent!");
    }
    
    public void ShowState()
    {
        Console.WriteLine("Current State:");
        Console.WriteLine($"  Client ID: {_clientId ?? "Not assigned"}");
        Console.WriteLine($"  Username: {_username ?? "Not assigned"}");
        Console.WriteLine($"  Password: {_password ?? "Not assigned"}");
        Console.WriteLine($"  Active Connections: {_connections.Count}");
        
        foreach (var conn in _connections.Values)
        {
            Console.WriteLine($"    - Connection ID: {conn.ConnectionId}");
            Console.WriteLine($"      Presenter: {conn.PresenterClientId}");
            Console.WriteLine($"      Viewers: {string.Join(", ", conn.ViewerClientIds)}");
        }
    }
}

sealed record ConnectionInfo(string ConnectionId, string PresenterClientId, List<string> ViewerClientIds);

enum TryConnectError
{
    ViewerNotFound,
    IncorrectUsernameOrPassword,
}

enum MessageDestination
{
    PresenterOnly,
    AllViewers,
    All,
}

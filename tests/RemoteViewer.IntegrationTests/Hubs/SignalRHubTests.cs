extern alias Server;

using Microsoft.AspNetCore.SignalR.Client;
using Nerdbank.MessagePack.SignalR;

using RemoteViewer.Shared;

namespace RemoteViewer.IntegrationTests.Hubs;

public class SignalRHubTests
{
    [ClassDataSource<ServerFixture>(Shared = SharedType.PerAssembly)]
    public required ServerFixture Server { get; init; }

    private async Task<HubConnection> CreateHubConnectionAsync(Action<HubConnection>? configure = null)
    {
        var hubUrl = $"{this.Server.ServerUrl}/connection";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Client-Version", ThisAssembly.AssemblyInformationalVersion);
            })
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider)
            .Build();

        configure?.Invoke(connection);

        await connection.StartAsync();

        return connection;
    }

    [Test]
    public async Task ClientConnectsToHubReceivesCredentials()
    {
        var credentials = new TaskCompletionSource<(string clientId, string username, string password)>();

        await using var connection = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials.TrySetResult((clientId, username, password));
            });
        });

        var result = await credentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result.clientId).IsNotNull().And.IsNotEmpty();
        await Assert.That(result.username.Replace(" ", "")).Length().IsEqualTo(10);
        await Assert.That(result.password).Length().IsEqualTo(8);
    }

    [Test]
    public async Task TwoClientsConnectGetDifferentCredentials()
    {
        var credentials1 = new TaskCompletionSource<(string clientId, string username, string password)>();
        var credentials2 = new TaskCompletionSource<(string clientId, string username, string password)>();

        await using var connection1 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials1.TrySetResult((clientId, username, password));
            });
        });

        await using var connection2 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials2.TrySetResult((clientId, username, password));
            });
        });

        var result1 = await credentials1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result2 = await credentials2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result1.clientId).IsNotEqualTo(result2.clientId);
        await Assert.That(result1.username).IsNotEqualTo(result2.username);
    }

    [Test]
    public async Task ViewerConnectsToPresenterBothReceiveConnectionStarted()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionStarted = new TaskCompletionSource<(string connectionId, bool isPresenter)>();
        var viewerConnectionStarted = new TaskCompletionSource<(string connectionId, bool isPresenter)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
            {
                presenterConnectionStarted.TrySetResult((connectionId, isPresenter));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
            {
                viewerConnectionStarted.TrySetResult((connectionId, isPresenter));
            });
        });

        var connectResult = await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);

        await Assert.That(connectResult).IsNull();

        var presenterResult = await presenterConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var viewerResult = await viewerConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(presenterResult.isPresenter).IsTrue();
        await Assert.That(viewerResult.isPresenter).IsFalse();
        await Assert.That(presenterResult.connectionId).IsEqualTo(viewerResult.connectionId);
    }

    [Test]
    public async Task ViewerConnectsWithWrongPasswordReturnsError()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
        });

        var connectResult = await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, "wrongpassword");

        await Assert.That(connectResult).IsEqualTo(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Test]
    public async Task ClientGeneratesNewPasswordReceivesNewCredentials()
    {
        var credentialsList = new List<string>();
        var credentialsReceived = new TaskCompletionSource();
        var secondCredentialsReceived = new TaskCompletionSource();

        await using var connection = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, password) =>
            {
                credentialsList.Add(password);
                if (credentialsList.Count == 1)
                    credentialsReceived.TrySetResult();
                else if (credentialsList.Count == 2)
                    secondCredentialsReceived.TrySetResult();
            });
        });

        await credentialsReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await connection.InvokeAsync("GenerateNewPassword");

        await secondCredentialsReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(credentialsList).Count().IsEqualTo(2);
        await Assert.That(credentialsList[0]).IsNotEqualTo(credentialsList[1]);
    }

    [Test]
    public async Task PresenterSendsMessageViewerReceivesIt()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var viewerReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewerReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        await presenter.InvokeAsync("SendMessage", connId, "test.message", testData, MessageDestination.AllViewers, (List<string>?)null);

        var received = await viewerReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(received.connectionId).IsEqualTo(connId);
        await Assert.That(received.messageType).IsEqualTo("test.message");
        await Assert.That(received.data).IsEquivalentTo(testData);
    }

    [Test]
    public async Task ViewerDisconnectsPresenterReceivesConnectionChanged()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var connectionChangedCount = 0;
        var viewerDisconnected = new TaskCompletionSource<ConnectionInfo>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<ConnectionInfo>("ConnectionChanged", (info) =>
            {
                connectionChangedCount++;
                if (connectionChangedCount >= 2 && info.Viewers.Count == 0)
                {
                    viewerDisconnected.TrySetResult(info);
                }
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disconnect viewer
        await viewer.DisposeAsync();

        var finalInfo = await viewerDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(finalInfo.Viewers).IsEmpty();
    }

    [Test]
    public async Task SetDisplayNameUpdatesAndBroadcasts()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var connectionChanged = new TaskCompletionSource<ConnectionInfo>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<ConnectionInfo>("ConnectionChanged", info =>
            {
                if (info.Presenter.DisplayName == "New Display Name")
                {
                    connectionChanged.TrySetResult(info);
                }
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await presenter.InvokeAsync("SetDisplayName", "New Display Name");

        var info = await connectionChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(info.Presenter.DisplayName).IsEqualTo("New Display Name");
    }

    [Test]
    public async Task SetConnectionPropertiesPresenterUpdatesProperties()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var connectionChanged = new TaskCompletionSource<ConnectionInfo>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<ConnectionInfo>("ConnectionChanged", info =>
            {
                if (info.Properties.CanSendSecureAttentionSequence)
                {
                    connectionChanged.TrySetResult(info);
                }
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var displays = new List<DisplayInfo>
        {
            new("display-1", "Primary Monitor", true, 0, 0, 1920, 1080)
        };
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: displays
        );

        await presenter.InvokeAsync("SetConnectionProperties", connId, properties);

        var info = await connectionChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(info.Properties.CanSendSecureAttentionSequence).IsTrue();
        await Assert.That(info.Properties.AvailableDisplays).Count().IsEqualTo(1);
    }

    [Test]
    public async Task GenerateIpcAuthTokenPresenterReceivesToken()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var token = await presenter.InvokeAsync<string?>("GenerateIpcAuthToken", connId);

        await Assert.That(token).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task GenerateIpcAuthTokenViewerReceivesNull()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var viewerConnectionStarted = new TaskCompletionSource();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) =>
            {
                viewerConnectionStarted.TrySetResult();
            });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await viewerConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Viewer tries to generate token - should fail
        var token = await viewer.InvokeAsync<string?>("GenerateIpcAuthToken", connId);

        await Assert.That(token).IsNull();
    }

    [Test]
    public async Task ConnectWithVersionMismatchReceivesVersionMismatch()
    {
        var versionMismatch = new TaskCompletionSource<(string serverVersion, string clientVersion)>();

        var hubUrl = $"{this.Server.ServerUrl}/connection";

        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Client-Version", "0.0.0-invalid");
            })
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider)
            .Build();

        connection.On<string, string>("VersionMismatch", (serverVersion, clientVersion) =>
        {
            versionMismatch.TrySetResult((serverVersion, clientVersion));
        });

        await connection.StartAsync();

        var result = await versionMismatch.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(result.clientVersion).IsEqualTo("0.0.0-invalid");
        await Assert.That(result.serverVersion).IsEqualTo(ThisAssembly.AssemblyInformationalVersion);

        await connection.DisposeAsync();
    }

    [Test]
    public async Task ConnectWithIpcTokenValidatesAndReturnsConnectionId()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();

        // First, create a presenter and viewer connection to get an IPC token
        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Generate IPC token
        var ipcToken = await presenter.InvokeAsync<string?>("GenerateIpcAuthToken", connId);
        await Assert.That(ipcToken).IsNotNull();

        // Now connect with the IPC token
        var ipcValidated = new TaskCompletionSource<string?>();

        var hubUrl = $"{this.Server.ServerUrl}/connection";
        var ipcConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Ipc-Token", ipcToken!);
            })
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider)
            .Build();

        ipcConnection.On<string?>("IpcTokenValidated", connectionId =>
        {
            ipcValidated.TrySetResult(connectionId);
        });

        await ipcConnection.StartAsync();

        var validatedConnectionId = await ipcValidated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(validatedConnectionId).IsEqualTo(connId);

        await ipcConnection.DisposeAsync();
    }

    [Test]
    public async Task ConnectWithInvalidIpcTokenReceivesNull()
    {
        var ipcValidated = new TaskCompletionSource<string?>();

        var hubUrl = $"{this.Server.ServerUrl}/connection";
        var ipcConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.Headers.Add("X-Ipc-Token", "invalid-token");
            })
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider)
            .Build();

        ipcConnection.On<string?>("IpcTokenValidated", connectionId =>
        {
            ipcValidated.TrySetResult(connectionId);
        });

        await ipcConnection.StartAsync();

        var validatedConnectionId = await ipcValidated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(validatedConnectionId).IsNull();

        await ipcConnection.DisposeAsync();
    }

    [Test]
    public async Task ViewerSendsMessagePresenterReceivesIt()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var presenterReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                presenterReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var testData = new byte[] { 1, 2, 3, 4, 5 };
        await viewer.InvokeAsync("SendMessage", connId, "viewer.message", testData, MessageDestination.PresenterOnly, (List<string>?)null);

        var received = await presenterReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(received.connectionId).IsEqualTo(connId);
        await Assert.That(received.messageType).IsEqualTo("viewer.message");
        await Assert.That(received.data).IsEquivalentTo(testData);
    }

    [Test]
    public async Task PresenterSendsMessageAllReceiveIt()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var presenterReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();
        var viewerReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                presenterReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewerReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var testData = new byte[] { 10, 20, 30 };
        await presenter.InvokeAsync("SendMessage", connId, "broadcast.all", testData, MessageDestination.All, (List<string>?)null);

        var presenterReceived = await presenterReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var viewerReceived = await viewerReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(presenterReceived.messageType).IsEqualTo("broadcast.all");
        await Assert.That(viewerReceived.messageType).IsEqualTo("broadcast.all");
        await Assert.That(presenterReceived.data).IsEquivalentTo(testData);
        await Assert.That(viewerReceived.data).IsEquivalentTo(testData);
    }

    [Test]
    public async Task ViewerSendsMessageAllExceptSenderReceiveIt()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var presenterReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();
        var viewer1ReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();
        var viewer2ReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                presenterReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer1 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewer1ReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await using var viewer2 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewer2ReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await viewer1.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        await viewer2.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var testData = new byte[] { 42, 43, 44 };
        // viewer1 sends - should NOT receive it back, but presenter and viewer2 should
        await viewer1.InvokeAsync("SendMessage", connId, "except.sender", testData, MessageDestination.AllExceptSender, (List<string>?)null);

        var presenterReceived = await presenterReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var viewer2Received = await viewer2ReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(presenterReceived.messageType).IsEqualTo("except.sender");
        await Assert.That(viewer2Received.messageType).IsEqualTo("except.sender");

        // viewer1 should NOT have received the message
        await Task.Delay(100);
        await Assert.That(viewer1ReceivedMessage.Task.IsCompleted).IsFalse();
    }

    [Test]
    public async Task PresenterSendsMessageToSpecificViewersOnly()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var connectionInfo = new TaskCompletionSource<ConnectionInfo>();
        var viewer1ReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();
        var viewer2ReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
            hub.On<string, bool>("ConnectionStarted", (connectionId, _) =>
            {
                presenterConnectionId.TrySetResult(connectionId);
            });
            hub.On<ConnectionInfo>("ConnectionChanged", info =>
            {
                if (info.Viewers.Count >= 2)
                {
                    connectionInfo.TrySetResult(info);
                }
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer1 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewer1ReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await using var viewer2 = await this.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
            hub.On<string, string, string, byte[]>("MessageReceived", (connectionId, senderId, messageType, data) =>
            {
                viewer2ReceivedMessage.TrySetResult((connectionId, senderId, messageType, data));
            });
        });

        await viewer1.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        await viewer2.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Wait for both viewers to be connected and get their IDs
        var info = await connectionInfo.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var viewer1Id = info.Viewers[0].ClientId;

        var testData = new byte[] { 100, 101, 102 };
        // Send to only viewer1
        await presenter.InvokeAsync("SendMessage", connId, "specific.target", testData, MessageDestination.SpecificClients, new List<string> { viewer1Id });

        var viewer1Received = await viewer1ReceivedMessage.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(viewer1Received.messageType).IsEqualTo("specific.target");
        await Assert.That(viewer1Received.data).IsEquivalentTo(testData);

        // viewer2 should NOT have received the message
        await Task.Delay(100);
        await Assert.That(viewer2ReceivedMessage.Task.IsCompleted).IsFalse();
    }
}

extern alias Server;

using Microsoft.AspNetCore.SignalR.Client;
using Nerdbank.MessagePack.SignalR;

using TryConnectError = Server::RemoteViewer.Server.SharedAPI.TryConnectError;
using ConnectionInfo = Server::RemoteViewer.Server.SharedAPI.ConnectionInfo;
using MessageDestination = Server::RemoteViewer.Server.SharedAPI.MessageDestination;
using Witness = Server::RemoteViewer.Server.SharedAPI.Witness;

namespace RemoteViewer.IntegrationTests.Hubs;

public class SignalRHubTests
{
    private static ServerFixture _server = null!;

    [Before(Class)]
    public static async Task SetupServer()
    {
        _server = new ServerFixture();
        await _server.StartAsync();
    }

    [After(Class)]
    public static async Task TeardownServer()
    {
        await _server.DisposeAsync();
    }

    private static async Task<HubConnection> CreateHubConnectionAsync(Action<HubConnection>? configure = null)
    {
        var hubUrl = $"{_server.ServerUrl}/connection";

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

        await using var connection = await CreateHubConnectionAsync(hub =>
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

        await using var connection1 = await CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials1.TrySetResult((clientId, username, password));
            });
        });

        await using var connection2 = await CreateHubConnectionAsync(hub =>
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

        await using var presenter = await CreateHubConnectionAsync(hub =>
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

        await using var viewer = await CreateHubConnectionAsync(hub =>
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

        await using var presenter = await CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await CreateHubConnectionAsync(hub =>
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

        await using var connection = await CreateHubConnectionAsync(hub =>
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

        await using var presenter = await CreateHubConnectionAsync(hub =>
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

        await using var viewer = await CreateHubConnectionAsync(hub =>
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

        await using var presenter = await CreateHubConnectionAsync(hub =>
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

        var viewer = await CreateHubConnectionAsync(hub =>
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
}

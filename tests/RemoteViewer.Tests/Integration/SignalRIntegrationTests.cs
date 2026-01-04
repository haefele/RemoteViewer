using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerdbank.MessagePack.SignalR;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Tests.Integration;

public class SignalRIntegrationTests : IClassFixture<SignalRIntegrationTests.TestServerFixture>
{
    private readonly TestServerFixture _fixture;

    public SignalRIntegrationTests(TestServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Client_ConnectsToHub_ReceivesCredentials()
    {
        var credentials = new TaskCompletionSource<(string clientId, string username, string password)>();

        await using var connection = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials.TrySetResult((clientId, username, password));
            });
        });

        var result = await credentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        result.clientId.Should().NotBeNullOrEmpty();
        result.username.Replace(" ", "").Should().HaveLength(10);
        result.password.Should().HaveLength(8);
    }

    [Fact]
    public async Task TwoClients_Connect_GetDifferentCredentials()
    {
        var credentials1 = new TaskCompletionSource<(string clientId, string username, string password)>();
        var credentials2 = new TaskCompletionSource<(string clientId, string username, string password)>();

        await using var connection1 = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials1.TrySetResult((clientId, username, password));
            });
        });

        await using var connection2 = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (clientId, username, password) =>
            {
                credentials2.TrySetResult((clientId, username, password));
            });
        });

        var result1 = await credentials1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result2 = await credentials2.Task.WaitAsync(TimeSpan.FromSeconds(5));

        result1.clientId.Should().NotBe(result2.clientId);
        result1.username.Should().NotBe(result2.username);
    }

    [Fact]
    public async Task Viewer_ConnectsToPresenter_BothReceiveConnectionStarted()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionStarted = new TaskCompletionSource<(string connectionId, bool isPresenter)>();
        var viewerConnectionStarted = new TaskCompletionSource<(string connectionId, bool isPresenter)>();

        await using var presenter = await _fixture.CreateHubConnectionAsync(hub =>
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

        await using var viewer = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (connectionId, isPresenter) =>
            {
                viewerConnectionStarted.TrySetResult((connectionId, isPresenter));
            });
        });

        var connectResult = await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);

        connectResult.Should().BeNull();

        var presenterResult = await presenterConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var viewerResult = await viewerConnectionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        presenterResult.isPresenter.Should().BeTrue();
        viewerResult.isPresenter.Should().BeFalse();
        presenterResult.connectionId.Should().Be(viewerResult.connectionId);
    }

    [Fact]
    public async Task Viewer_ConnectsWithWrongPassword_ReturnsError()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();

        await using var presenter = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, username, password) =>
            {
                presenterCredentials.TrySetResult((username.Replace(" ", ""), password));
            });
        });

        var creds = await presenterCredentials.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using var viewer = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
        });

        var connectResult = await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, "wrongpassword");

        connectResult.Should().Be(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Fact]
    public async Task Client_GeneratesNewPassword_ReceivesNewCredentials()
    {
        var credentialsList = new List<string>();
        var credentialsReceived = new TaskCompletionSource();
        var secondCredentialsReceived = new TaskCompletionSource();

        await using var connection = await _fixture.CreateHubConnectionAsync(hub =>
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

        credentialsList.Should().HaveCount(2);
        credentialsList[0].Should().NotBe(credentialsList[1]);
    }

    [Fact]
    public async Task Presenter_SendsMessage_ViewerReceivesIt()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var viewerReceivedMessage = new TaskCompletionSource<(string connectionId, string senderId, string messageType, byte[] data)>();

        await using var presenter = await _fixture.CreateHubConnectionAsync(hub =>
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

        await using var viewer = await _fixture.CreateHubConnectionAsync(hub =>
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

        received.connectionId.Should().Be(connId);
        received.messageType.Should().Be("test.message");
        received.data.Should().Equal(testData);
    }

    [Fact]
    public async Task Viewer_Disconnects_PresenterReceivesConnectionChanged()
    {
        var presenterCredentials = new TaskCompletionSource<(string username, string password)>();
        var presenterConnectionId = new TaskCompletionSource<string>();
        var connectionChangedCount = 0;
        var viewerDisconnected = new TaskCompletionSource<ConnectionInfo>();

        await using var presenter = await _fixture.CreateHubConnectionAsync(hub =>
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

        var viewer = await _fixture.CreateHubConnectionAsync(hub =>
        {
            hub.On<string, string, string>("CredentialsAssigned", (_, _, _) => { });
            hub.On<string, bool>("ConnectionStarted", (_, _) => { });
        });

        await viewer.InvokeAsync<TryConnectError?>("ConnectTo", creds.username, creds.password);
        var connId = await presenterConnectionId.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Disconnect viewer
        await viewer.DisposeAsync();

        var finalInfo = await viewerDisconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));

        finalInfo.Viewers.Should().BeEmpty();
    }

    public class TestServerFixture : IAsyncLifetime
    {
        private WebApplication? _app;
        private string? _serverUrl;

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();

            builder.WebHost.UseUrls("http://127.0.0.1:0"); // Use random available port

            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton<IConnectionsService, ConnectionsService>();
            builder.Services.AddSingleton<IIpcTokenService, IpcTokenService>();
            builder.Services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
            builder.Services
                .AddSignalR(f =>
                {
                    f.MaximumReceiveMessageSize = null;
                })
                .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider);

            _app = builder.Build();

            _app.MapHub<ConnectionHub>("/connection");

            await _app.StartAsync();

            _serverUrl = _app.Urls.First();
        }

        public async Task DisposeAsync()
        {
            if (_app != null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
        }

        public async Task<HubConnection> CreateHubConnectionAsync(Action<HubConnection>? configure = null)
        {
            var hubUrl = $"{_serverUrl}/connection";

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
    }
}

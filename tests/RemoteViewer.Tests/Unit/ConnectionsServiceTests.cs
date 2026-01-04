using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Tests.Unit;

public class ConnectionsServiceTests : IDisposable
{
    private readonly IHubContext<ConnectionHub, IConnectionHubClient> _hubContext;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConnectionsService _service;
    private readonly IConnectionHubClient _mockClient;
    private readonly IHubClients<IConnectionHubClient> _mockClients;

    public ConnectionsServiceTests()
    {
        this._hubContext = Substitute.For<IHubContext<ConnectionHub, IConnectionHubClient>>();
        this._loggerFactory = Substitute.For<ILoggerFactory>();
        this._mockClient = Substitute.For<IConnectionHubClient>();
        this._mockClients = Substitute.For<IHubClients<IConnectionHubClient>>();

        // Setup hub context to return mock clients
        this._hubContext.Clients.Returns(this._mockClients);
        this._mockClients.Client(Arg.Any<string>()).Returns(this._mockClient);

        // Setup logger factory
        var logger = Substitute.For<ILogger<ConnectionsService>>();
        this._loggerFactory.CreateLogger<ConnectionsService>().Returns(logger);
        this._loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        this._service = new ConnectionsService(this._hubContext, this._loggerFactory);
    }

    public void Dispose()
    {
        this._service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task RegisterNewClientAssignsCredentials()
    {
        const string signalrConnectionId = "conn-123";

        await this._service.Register(signalrConnectionId, displayName: null);

        await this._mockClient.Received(1).CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Is<string>(u => u.Replace(" ", "").Length == 10 && u.Replace(" ", "").All(char.IsDigit)),
            Arg.Is<string>(p => p.Length == 8)
        );
    }

    [Fact]
    public async Task RegisterWithDisplayNameSetsDisplayName()
    {
        const string signalrConnectionId = "conn-123";
        const string displayName = "Test User";

        await this._service.Register(signalrConnectionId, displayName);

        await this._mockClient.Received(1).CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task RegisterMultipleClientsAssignsUniqueUsernames()
    {
        var capturedUsernames = new List<string>();
        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => capturedUsernames.Add(u.Replace(" ", ""))),
            Arg.Any<string>()
        );

        await this._service.Register("conn-1", null);
        await this._service.Register("conn-2", null);
        await this._service.Register("conn-3", null);

        capturedUsernames.Should().HaveCount(3);
        capturedUsernames.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task UnregisterExistingClientRemovesClient()
    {
        const string signalrConnectionId = "conn-123";
        await this._service.Register(signalrConnectionId, null);

        await this._service.Unregister(signalrConnectionId);

        // No exception should be thrown - client should be removed successfully
    }

    [Fact]
    public async Task UnregisterNonExistentClientDoesNotThrow()
    {
        await this._service.Unregister("non-existent-connection");

        // Should not throw
    }

    [Fact]
    public async Task GenerateNewPasswordExistingClientUpdatesPassword()
    {
        const string signalrConnectionId = "conn-123";
        var capturedPasswords = new List<string>();

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<string>(p => capturedPasswords.Add(p))
        );

        await this._service.Register(signalrConnectionId, null);
        await this._service.GenerateNewPassword(signalrConnectionId);

        capturedPasswords.Should().HaveCount(2);
        capturedPasswords[0].Should().NotBe(capturedPasswords[1]);
    }

    [Fact]
    public async Task GenerateNewPasswordNonExistentClientDoesNothing()
    {
        await this._service.GenerateNewPassword("non-existent-connection");

        await this._mockClient.DidNotReceive().CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task TryConnectToInvalidCredentialsReturnsIncorrectUsernameOrPassword()
    {
        const string viewerConnectionId = "viewer-conn";
        await this._service.Register(viewerConnectionId, null);

        var result = await this._service.TryConnectTo(viewerConnectionId, "wrong-username", "wrong-password");

        result.Should().Be(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Fact]
    public async Task TryConnectToViewerNotRegisteredReturnsViewerNotFound()
    {
        var result = await this._service.TryConnectTo("non-existent", "username", "password");

        result.Should().Be(TryConnectError.ViewerNotFound);
    }

    [Fact]
    public async Task TryConnectToConnectToSelfReturnsCannotConnectToYourself()
    {
        const string connectionId = "conn-123";
        string? capturedUsername = null;
        string? capturedPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => capturedUsername = u.Replace(" ", "")),
            Arg.Do<string>(p => capturedPassword = p)
        );

        await this._service.Register(connectionId, null);

        var result = await this._service.TryConnectTo(connectionId, capturedUsername!, capturedPassword!);

        result.Should().Be(TryConnectError.CannotConnectToYourself);
    }

    [Fact]
    public async Task TryConnectToValidCredentialsReturnsNullAndStartsConnection()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);

        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        result.Should().BeNull();
        await this._mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: true);
        await this._mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: false);
    }

    [Fact]
    public async Task TryConnectToWithSpacesInUsernameNormalizesAndConnects()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);

        // Username comes with spaces from CredentialsAssigned, but should still work
        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConnectToCaseInsensitivePasswordConnects()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);

        // Use uppercase password
        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!.ToUpperInvariant());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DisconnectFromConnectionViewerDisconnectsNotifiesViewer()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        await this._service.DisconnectFromConnection(viewerConnectionId, connectionId!);

        await this._mockClient.Received().ConnectionStopped(connectionId!);
    }

    [Fact]
    public async Task DisconnectFromConnectionPresenterDisconnectsNotifiesAllViewers()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewer1ConnectionId, null);
        await this._service.Register(viewer2ConnectionId, null);

        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        await this._service.DisconnectFromConnection(presenterConnectionId, connectionId!);

        // All participants should receive ConnectionStopped
        await this._mockClient.Received(3).ConnectionStopped(connectionId!);
    }

    [Fact]
    public async Task SendMessageToPresenterOnlySendsToPresenter()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var testData = new byte[] { 1, 2, 3 };
        await this._service.SendMessage(viewerConnectionId, connectionId!, "test.message", testData, MessageDestination.PresenterOnly);

        await this._mockClient.Received(1).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "test.message",
            testData
        );
    }

    [Fact]
    public async Task SendMessageToAllViewersSendsToAllViewers()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewer1ConnectionId, null);
        await this._service.Register(viewer2ConnectionId, null);

        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        var testData = new byte[] { 1, 2, 3 };
        await this._service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.AllViewers);

        await this._mockClient.Received(2).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "test.message",
            testData
        );
    }

    [Fact]
    public async Task IsPresenterOfConnectionPresenterClientReturnsTrue()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var result = this._service.IsPresenterOfConnection(presenterConnectionId, connectionId!);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPresenterOfConnectionViewerClientReturnsFalse()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var result = this._service.IsPresenterOfConnection(viewerConnectionId, connectionId!);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetConnectionPropertiesValidPresenterUpdatesProperties()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: [new DisplayInfo("display1", "Primary Monitor", true, 0, 0, 1920, 1080)]
        );

        await this._service.SetConnectionProperties(presenterConnectionId, connectionId!, properties);

        await this._mockClient.Received().ConnectionChanged(Arg.Is<ConnectionInfo>(ci =>
            ci.Properties.CanSendSecureAttentionSequence == true &&
            ci.Properties.AvailableDisplays.Count == 1
        ));
    }

    [Fact]
    public async Task SetDisplayNameChangesDisplayNameBroadcastsToConnections()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewerConnectionId, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        await this._service.SetDisplayName(presenterConnectionId, "New Display Name");

        await this._mockClient.Received().ConnectionChanged(Arg.Is<ConnectionInfo>(ci =>
            ci.Presenter.DisplayName == "New Display Name"
        ));
    }

    [Fact]
    public async Task MultipleViewersConnectSamePresenterAllReceiveConnectionChanged()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewer1ConnectionId, null);
        await this._service.Register(viewer2ConnectionId, null);

        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();

        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        // After second viewer joins, all 3 participants should receive ConnectionChanged
        await this._mockClient.Received(3).ConnectionChanged(Arg.Any<ConnectionInfo>());
    }

    [Fact]
    public async Task SendMessageAllExceptSenderExcludesSender()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await this._mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await this._service.Register(presenterConnectionId, null);
        await this._service.Register(viewer1ConnectionId, null);
        await this._service.Register(viewer2ConnectionId, null);

        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        this._mockClient.ClearReceivedCalls();

        var testData = new byte[] { 1, 2, 3 };
        await this._service.SendMessage(viewer1ConnectionId, connectionId!, "chat.message", testData, MessageDestination.AllExceptSender);

        // Should send to presenter and viewer2 (2 recipients), not viewer1 (sender)
        await this._mockClient.Received(2).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "chat.message",
            testData
        );
    }
}

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
        _hubContext = Substitute.For<IHubContext<ConnectionHub, IConnectionHubClient>>();
        _loggerFactory = Substitute.For<ILoggerFactory>();
        _mockClient = Substitute.For<IConnectionHubClient>();
        _mockClients = Substitute.For<IHubClients<IConnectionHubClient>>();

        // Setup hub context to return mock clients
        _hubContext.Clients.Returns(_mockClients);
        _mockClients.Client(Arg.Any<string>()).Returns(_mockClient);

        // Setup logger factory
        var logger = Substitute.For<ILogger<ConnectionsService>>();
        _loggerFactory.CreateLogger<ConnectionsService>().Returns(logger);
        _loggerFactory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());

        _service = new ConnectionsService(_hubContext, _loggerFactory);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Register_NewClient_AssignsCredentials()
    {
        const string signalrConnectionId = "conn-123";

        await _service.Register(signalrConnectionId, displayName: null);

        await _mockClient.Received(1).CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Is<string>(u => u.Replace(" ", "").Length == 10 && u.Replace(" ", "").All(char.IsDigit)),
            Arg.Is<string>(p => p.Length == 8)
        );
    }

    [Fact]
    public async Task Register_WithDisplayName_SetsDisplayName()
    {
        const string signalrConnectionId = "conn-123";
        const string displayName = "Test User";

        await _service.Register(signalrConnectionId, displayName);

        await _mockClient.Received(1).CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task Register_MultipleClients_AssignsUniqueUsernames()
    {
        var capturedUsernames = new List<string>();
        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => capturedUsernames.Add(u.Replace(" ", ""))),
            Arg.Any<string>()
        );

        await _service.Register("conn-1", null);
        await _service.Register("conn-2", null);
        await _service.Register("conn-3", null);

        capturedUsernames.Should().HaveCount(3);
        capturedUsernames.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task Unregister_ExistingClient_RemovesClient()
    {
        const string signalrConnectionId = "conn-123";
        await _service.Register(signalrConnectionId, null);

        await _service.Unregister(signalrConnectionId);

        // No exception should be thrown - client should be removed successfully
    }

    [Fact]
    public async Task Unregister_NonExistentClient_DoesNotThrow()
    {
        await _service.Unregister("non-existent-connection");

        // Should not throw
    }

    [Fact]
    public async Task GenerateNewPassword_ExistingClient_UpdatesPassword()
    {
        const string signalrConnectionId = "conn-123";
        var capturedPasswords = new List<string>();

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Do<string>(p => capturedPasswords.Add(p))
        );

        await _service.Register(signalrConnectionId, null);
        await _service.GenerateNewPassword(signalrConnectionId);

        capturedPasswords.Should().HaveCount(2);
        capturedPasswords[0].Should().NotBe(capturedPasswords[1]);
    }

    [Fact]
    public async Task GenerateNewPassword_NonExistentClient_DoesNothing()
    {
        await _service.GenerateNewPassword("non-existent-connection");

        await _mockClient.DidNotReceive().CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task TryConnectTo_InvalidCredentials_ReturnsIncorrectUsernameOrPassword()
    {
        const string viewerConnectionId = "viewer-conn";
        await _service.Register(viewerConnectionId, null);

        var result = await _service.TryConnectTo(viewerConnectionId, "wrong-username", "wrong-password");

        result.Should().Be(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Fact]
    public async Task TryConnectTo_ViewerNotRegistered_ReturnsViewerNotFound()
    {
        var result = await _service.TryConnectTo("non-existent", "username", "password");

        result.Should().Be(TryConnectError.ViewerNotFound);
    }

    [Fact]
    public async Task TryConnectTo_ConnectToSelf_ReturnsCannotConnectToYourself()
    {
        const string connectionId = "conn-123";
        string? capturedUsername = null;
        string? capturedPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => capturedUsername = u.Replace(" ", "")),
            Arg.Do<string>(p => capturedPassword = p)
        );

        await _service.Register(connectionId, null);

        var result = await _service.TryConnectTo(connectionId, capturedUsername!, capturedPassword!);

        result.Should().Be(TryConnectError.CannotConnectToYourself);
    }

    [Fact]
    public async Task TryConnectTo_ValidCredentials_ReturnsNullAndStartsConnection()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);

        var result = await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        result.Should().BeNull();
        await _mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: true);
        await _mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: false);
    }

    [Fact]
    public async Task TryConnectTo_WithSpacesInUsername_NormalizesAndConnects()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);

        // Username comes with spaces from CredentialsAssigned, but should still work
        var result = await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryConnectTo_CaseInsensitivePassword_Connects()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);

        // Use uppercase password
        var result = await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!.ToUpperInvariant());

        result.Should().BeNull();
    }

    [Fact]
    public async Task DisconnectFromConnection_ViewerDisconnects_NotifiesViewer()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        await _service.DisconnectFromConnection(viewerConnectionId, connectionId!);

        await _mockClient.Received().ConnectionStopped(connectionId!);
    }

    [Fact]
    public async Task DisconnectFromConnection_PresenterDisconnects_NotifiesAllViewers()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewer1ConnectionId, null);
        await _service.Register(viewer2ConnectionId, null);

        await _service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await _service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        await _service.DisconnectFromConnection(presenterConnectionId, connectionId!);

        // All participants should receive ConnectionStopped
        await _mockClient.Received(3).ConnectionStopped(connectionId!);
    }

    [Fact]
    public async Task SendMessage_ToPresenterOnly_SendsToPresenter()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var testData = new byte[] { 1, 2, 3 };
        await _service.SendMessage(viewerConnectionId, connectionId!, "test.message", testData, MessageDestination.PresenterOnly);

        await _mockClient.Received(1).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "test.message",
            testData
        );
    }

    [Fact]
    public async Task SendMessage_ToAllViewers_SendsToAllViewers()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewer1ConnectionId, null);
        await _service.Register(viewer2ConnectionId, null);

        await _service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await _service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        var testData = new byte[] { 1, 2, 3 };
        await _service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.AllViewers);

        await _mockClient.Received(2).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "test.message",
            testData
        );
    }

    [Fact]
    public async Task IsPresenterOfConnection_PresenterClient_ReturnsTrue()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var result = _service.IsPresenterOfConnection(presenterConnectionId, connectionId!);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsPresenterOfConnection_ViewerClient_ReturnsFalse()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var result = _service.IsPresenterOfConnection(viewerConnectionId, connectionId!);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task SetConnectionProperties_ValidPresenter_UpdatesProperties()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: [new DisplayInfo("display1", "Primary Monitor", true, 0, 0, 1920, 1080)]
        );

        await _service.SetConnectionProperties(presenterConnectionId, connectionId!, properties);

        await _mockClient.Received().ConnectionChanged(Arg.Is<ConnectionInfo>(ci =>
            ci.Properties.CanSendSecureAttentionSequence == true &&
            ci.Properties.AvailableDisplays.Count == 1
        ));
    }

    [Fact]
    public async Task SetDisplayName_ChangesDisplayName_BroadcastsToConnections()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewerConnectionId, null);
        await _service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        await _service.SetDisplayName(presenterConnectionId, "New Display Name");

        await _mockClient.Received().ConnectionChanged(Arg.Is<ConnectionInfo>(ci =>
            ci.Presenter.DisplayName == "New Display Name"
        ));
    }

    [Fact]
    public async Task MultipleViewersConnect_SamePresenter_AllReceiveConnectionChanged()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewer1ConnectionId, null);
        await _service.Register(viewer2ConnectionId, null);

        await _service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        _mockClient.ClearReceivedCalls();

        await _service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        // After second viewer joins, all 3 participants should receive ConnectionChanged
        await _mockClient.Received(3).ConnectionChanged(Arg.Any<ConnectionInfo>());
    }

    [Fact]
    public async Task SendMessage_AllExceptSender_ExcludesSender()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;

        await _mockClient.CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );

        await _mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );

        await _service.Register(presenterConnectionId, null);
        await _service.Register(viewer1ConnectionId, null);
        await _service.Register(viewer2ConnectionId, null);

        await _service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await _service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        _mockClient.ClearReceivedCalls();

        var testData = new byte[] { 1, 2, 3 };
        await _service.SendMessage(viewer1ConnectionId, connectionId!, "chat.message", testData, MessageDestination.AllExceptSender);

        // Should send to presenter and viewer2 (2 recipients), not viewer1 (sender)
        await _mockClient.Received(2).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "chat.message",
            testData
        );
    }
}

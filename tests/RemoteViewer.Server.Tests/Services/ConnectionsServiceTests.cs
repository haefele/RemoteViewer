using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Server.Tests.Services;

public class ConnectionsServiceTests : IDisposable
{
    private IHubContext<ConnectionHub, IConnectionHubClient> _hubContext = null!;
    private ILoggerFactory _loggerFactory = null!;
    private ConnectionsService _service = null!;
    private IConnectionHubClient _mockClient = null!;
    private IHubClients<IConnectionHubClient> _mockClients = null!;

    [Before(Test)]
    public void Setup()
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
        this._service?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
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

    [Test]
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

    [Test]
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

        await Assert.That(capturedUsernames).Count().IsEqualTo(3);
        await Assert.That(capturedUsernames.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task UnregisterExistingClientRemovesClient()
    {
        const string signalrConnectionId = "conn-123";
        await this._service.Register(signalrConnectionId, null);

        await this._service.Unregister(signalrConnectionId);

        // No exception should be thrown - client should be removed successfully
    }

    [Test]
    public async Task UnregisterNonExistentClientDoesNotThrow()
    {
        await this._service.Unregister("non-existent-connection");

        // Should not throw
    }

    [Test]
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

        await Assert.That(capturedPasswords).Count().IsEqualTo(2);
        await Assert.That(capturedPasswords[0]).IsNotEqualTo(capturedPasswords[1]);
    }

    [Test]
    public async Task GenerateNewPasswordNonExistentClientDoesNothing()
    {
        await this._service.GenerateNewPassword("non-existent-connection");

        await this._mockClient.DidNotReceive().CredentialsAssigned(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>()
        );
    }

    [Test]
    public async Task TryConnectToInvalidCredentialsReturnsIncorrectUsernameOrPassword()
    {
        const string viewerConnectionId = "viewer-conn";
        await this._service.Register(viewerConnectionId, null);

        var result = await this._service.TryConnectTo(viewerConnectionId, "wrong-username", "wrong-password");

        await Assert.That(result).IsEqualTo(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Test]
    public async Task TryConnectToViewerNotRegisteredReturnsViewerNotFound()
    {
        var result = await this._service.TryConnectTo("non-existent", "username", "password");

        await Assert.That(result).IsEqualTo(TryConnectError.ViewerNotFound);
    }

    [Test]
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

        await Assert.That(result).IsEqualTo(TryConnectError.CannotConnectToYourself);
    }

    [Test]
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

        await Assert.That(result).IsNull();
        await this._mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: true);
        await this._mockClient.Received().ConnectionStarted(Arg.Any<string>(), isPresenter: false);
    }

    [Test]
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

        await Assert.That(result).IsNull();
    }

    [Test]
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

        await Assert.That(result).IsNull();
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

        await Assert.That(result).IsTrue();
    }

    [Test]
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

        await Assert.That(result).IsFalse();
    }

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

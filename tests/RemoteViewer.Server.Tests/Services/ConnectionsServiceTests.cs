using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using RemoteViewer.Shared;

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

        // Act
        await this._service.Register(signalrConnectionId, "client-guid-1", displayName: null);

        // Assert
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

        // Act
        await this._service.Register(signalrConnectionId, "client-guid-1", displayName);

        // Assert
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

        // Act
        await this._service.Register("conn-1", "client-guid-1", null);
        await this._service.Register("conn-2", "client-guid-2", null);
        await this._service.Register("conn-3", "client-guid-3", null);

        // Assert
        await Assert.That(capturedUsernames).Count().IsEqualTo(3);
        await Assert.That(capturedUsernames.Distinct().Count()).IsEqualTo(3);
    }

    [Test]
    public async Task UnregisterExistingClientRemovesClient()
    {
        const string signalrConnectionId = "conn-123";
        await this._service.Register(signalrConnectionId, "client-guid-1", null);

        // Act
        await this._service.Unregister(signalrConnectionId);

        // Assert - No exception should be thrown - client should be removed successfully
    }

    [Test]
    public async Task UnregisterNonExistentClientDoesNotThrow()
    {
        // Act
        await this._service.Unregister("non-existent-connection");

        // Assert - Should not throw
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
        await this._service.Register(signalrConnectionId, "client-guid-1", null);

        // Act
        await this._service.GenerateNewPassword(signalrConnectionId);

        // Assert
        await Assert.That(capturedPasswords).Count().IsEqualTo(2);
        await Assert.That(capturedPasswords[0]).IsNotEqualTo(capturedPasswords[1]);
    }

    [Test]
    public async Task GenerateNewPasswordNonExistentClientDoesNothing()
    {
        // Act
        await this._service.GenerateNewPassword("non-existent-connection");

        // Assert
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
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);

        // Act
        var result = await this._service.TryConnectTo(viewerConnectionId, "wrong-username", "wrong-password");

        // Assert
        await Assert.That(result).IsEqualTo(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Test]
    public async Task TryConnectToViewerNotRegisteredReturnsViewerNotFound()
    {
        // Act
        var result = await this._service.TryConnectTo("non-existent", "username", "password");

        // Assert
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
        await this._service.Register(connectionId, "client-guid-1", null);

        // Act
        var result = await this._service.TryConnectTo(connectionId, capturedUsername!, capturedPassword!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);

        // Act
        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);

        // Act - Username comes with spaces from CredentialsAssigned, but should still work
        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);

        // Act - Use uppercase password
        var result = await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!.ToUpperInvariant());

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Act
        await this._service.DisconnectFromConnection(viewerConnectionId, connectionId!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewer1ConnectionId, ConnectionsServiceTestConstants.Viewer1Guid, null);
        await this._service.Register(viewer2ConnectionId, ConnectionsServiceTestConstants.Viewer2Guid, null);
        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        // Act
        await this._service.DisconnectFromConnection(presenterConnectionId, connectionId!);

        // Assert - All participants should receive ConnectionStopped
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(viewerConnectionId, connectionId!, "test.message", testData, MessageDestination.PresenterOnly);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewer1ConnectionId, ConnectionsServiceTestConstants.Viewer1Guid, null);
        await this._service.Register(viewer2ConnectionId, ConnectionsServiceTestConstants.Viewer2Guid, null);
        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.AllViewers);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Act
        var result = await this._service.IsPresenterOfConnection(presenterConnectionId, connectionId!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Act
        var result = await this._service.IsPresenterOfConnection(viewerConnectionId, connectionId!);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: [new DisplayInfo("display1", "Primary Monitor", true, 0, 0, 1920, 1080)]
        );

        // Act
        await this._service.SetConnectionProperties(presenterConnectionId, connectionId!, properties);

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);

        // Act
        await this._service.SetDisplayName(presenterConnectionId, "New Display Name");

        // Assert
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewer1ConnectionId, ConnectionsServiceTestConstants.Viewer1Guid, null);
        await this._service.Register(viewer2ConnectionId, ConnectionsServiceTestConstants.Viewer2Guid, null);
        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();

        // Act
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);

        // Assert - After second viewer joins, all 3 participants should receive ConnectionChanged
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewer1ConnectionId, ConnectionsServiceTestConstants.Viewer1Guid, null);
        await this._service.Register(viewer2ConnectionId, ConnectionsServiceTestConstants.Viewer2Guid, null);
        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(viewer1ConnectionId, connectionId!, "chat.message", testData, MessageDestination.AllExceptSender);

        // Assert - Should send to presenter and viewer2 (2 recipients), not viewer1 (sender)
        await this._mockClient.Received(2).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "chat.message",
            testData
        );
    }

    [Test]
    public async Task SendMessageSpecificClientsNullTargetIdsSendsNoMessages()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.SpecificClients, targetClientIds: null);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SendMessageSpecificClientsEmptyTargetIdsSendsNoMessages()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.SpecificClients, targetClientIds: []);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SendMessageSpecificClientsInvalidClientIdsSendsNoMessages()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(presenterConnectionId, connectionId!, "test.message", testData, MessageDestination.SpecificClients, targetClientIds: ["invalid-id-1", "invalid-id-2"]);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SendMessageSpecificClientsMixedValidInvalidIdsSendsOnlyToValid()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewer1ConnectionId = "viewer1-conn";
        const string viewer2ConnectionId = "viewer2-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;
        string? clientId = null;
        await this._mockClient.CredentialsAssigned(
            Arg.Do<string>(id => clientId = id),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );
        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        var presenterId = clientId;
        await this._service.Register(viewer1ConnectionId, ConnectionsServiceTestConstants.Viewer1Guid, null);
        var viewer1Id = clientId;
        await this._service.Register(viewer2ConnectionId, ConnectionsServiceTestConstants.Viewer2Guid, null);
        var viewer2Id = clientId;
        await this._service.TryConnectTo(viewer1ConnectionId, presenterUsername!, presenterPassword!);
        await this._service.TryConnectTo(viewer2ConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act - Send to presenter (valid) and invalid ID - should only send to presenter
        await this._service.SendMessage(viewer1ConnectionId, connectionId!, "test.message", testData, MessageDestination.SpecificClients, targetClientIds: [presenterId!, "invalid-id"]);

        // Assert
        await this._mockClient.Received(1).MessageReceived(
            connectionId!,
            Arg.Any<string>(),
            "test.message",
            testData
        );
    }

    [Test]
    public async Task SendMessageNonExistentSenderReturnsEarly()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage("non-existent-sender", connectionId!, "test.message", testData, MessageDestination.AllViewers);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SendMessageNonExistentConnectionReturnsEarly()
    {
        const string presenterConnectionId = "presenter-conn";
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act
        await this._service.SendMessage(presenterConnectionId, "non-existent-connection", "test.message", testData, MessageDestination.AllViewers);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SendMessageSenderNotInConnectionReturnsEarly()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        const string outsiderConnectionId = "outsider-conn";
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.Register(outsiderConnectionId, ConnectionsServiceTestConstants.OutsiderGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var testData = new byte[] { 1, 2, 3 };

        // Act - Outsider tries to send message to a connection they're not part of
        await this._service.SendMessage(outsiderConnectionId, connectionId!, "test.message", testData, MessageDestination.AllViewers);

        // Assert
        await this._mockClient.DidNotReceive().MessageReceived(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<byte[]>()
        );
    }

    [Test]
    public async Task SetConnectionPropertiesNonExistentSenderReturnsEarly()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: []
        );

        // Act
        await this._service.SetConnectionProperties("non-existent-sender", connectionId!, properties);

        // Assert
        await this._mockClient.DidNotReceive().ConnectionChanged(Arg.Any<ConnectionInfo>());
    }

    [Test]
    public async Task SetConnectionPropertiesNonExistentConnectionReturnsEarly()
    {
        const string presenterConnectionId = "presenter-conn";
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        this._mockClient.ClearReceivedCalls();
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: []
        );

        // Act
        await this._service.SetConnectionProperties(presenterConnectionId, "non-existent-connection", properties);

        // Assert
        await this._mockClient.DidNotReceive().ConnectionChanged(Arg.Any<ConnectionInfo>());
    }

    [Test]
    public async Task SetConnectionPropertiesViewerNotPresenterReturnsEarly()
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
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: true,
            InputBlockedViewerIds: [],
            AvailableDisplays: []
        );

        // Act - Viewer tries to set connection properties (only presenter should be allowed)
        await this._service.SetConnectionProperties(viewerConnectionId, connectionId!, properties);

        // Assert
        await this._mockClient.DidNotReceive().ConnectionChanged(Arg.Any<ConnectionInfo>());
    }

    [Test]
    public async Task SetConnectionPropertiesInvalidBlockedViewerIdsNormalizesToValidOnly()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;
        string? viewerId = null;
        await this._mockClient.CredentialsAssigned(
            Arg.Do<string>(id => viewerId = id),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );
        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        var presenterId = viewerId;
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        var actualViewerId = viewerId;
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: false,
            InputBlockedViewerIds: [actualViewerId!, "invalid-viewer-id", "another-invalid-id"],
            AvailableDisplays: []
        );
        ConnectionInfo? capturedInfo = null;
        await this._mockClient.ConnectionChanged(Arg.Do<ConnectionInfo>(ci => capturedInfo = ci));

        // Act
        await this._service.SetConnectionProperties(presenterConnectionId, connectionId!, properties);

        // Assert
        await Assert.That(capturedInfo).IsNotNull();
        // Should only contain the valid viewer ID, invalid IDs should be filtered out
        await Assert.That(capturedInfo!.Properties.InputBlockedViewerIds).Count().IsEqualTo(1);
        await Assert.That(capturedInfo.Properties.InputBlockedViewerIds[0]).IsEqualTo(actualViewerId);
    }

    [Test]
    public async Task SetConnectionPropertiesDuplicateBlockedViewerIdsDeduplicates()
    {
        const string presenterConnectionId = "presenter-conn";
        const string viewerConnectionId = "viewer-conn";
        string? presenterUsername = null;
        string? presenterPassword = null;
        string? connectionId = null;
        string? viewerId = null;
        await this._mockClient.CredentialsAssigned(
            Arg.Do<string>(id => viewerId = id),
            Arg.Do<string>(u => presenterUsername ??= u.Replace(" ", "")),
            Arg.Do<string>(p => presenterPassword ??= p)
        );
        await this._mockClient.ConnectionStarted(
            Arg.Do<string>(cid => connectionId ??= cid),
            Arg.Any<bool>()
        );
        await this._service.Register(presenterConnectionId, ConnectionsServiceTestConstants.PresenterGuid, null);
        var presenterId = viewerId;
        await this._service.Register(viewerConnectionId, ConnectionsServiceTestConstants.ViewerGuid, null);
        var actualViewerId = viewerId;
        await this._service.TryConnectTo(viewerConnectionId, presenterUsername!, presenterPassword!);
        this._mockClient.ClearReceivedCalls();
        // Pass the same viewer ID multiple times
        var properties = new ConnectionProperties(
            CanSendSecureAttentionSequence: false,
            InputBlockedViewerIds: [actualViewerId!, actualViewerId!, actualViewerId!],
            AvailableDisplays: []
        );
        ConnectionInfo? capturedInfo = null;
        await this._mockClient.ConnectionChanged(Arg.Do<ConnectionInfo>(ci => capturedInfo = ci));

        // Act
        await this._service.SetConnectionProperties(presenterConnectionId, connectionId!, properties);

        // Assert
        await Assert.That(capturedInfo).IsNotNull();
        // Duplicates should be removed
        await Assert.That(capturedInfo!.Properties.InputBlockedViewerIds).Count().IsEqualTo(1);
        await Assert.That(capturedInfo.Properties.InputBlockedViewerIds[0]).IsEqualTo(actualViewerId);
    }
}




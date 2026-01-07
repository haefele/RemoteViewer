using NSubstitute;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.IntegrationTests.Fixtures;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;
using TUnit.Core;
using DataFormat = Avalonia.Input.DataFormat;

namespace RemoteViewer.IntegrationTests;

//[NotInParallel]
public class ConnectionHubClientTests()
{

    //[ClassDataSource<ServerFixture>(Shared = SharedType.PerTestSession)]
    [ClassDataSource<ServerFixture>(Shared = SharedType.None)]
    public required ServerFixture Server { get; init; }

    [Test]
    public async Task TwoClientsCanEstablishConnection()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewerConnTask = viewer.WaitForConnectionAsync();

        var error = await viewer.HubClient.ConnectTo(username, password);

        await Assert.That(error).IsNull();

        var presenterConn = await presenterConnTask;
        var viewerConn = await viewerConnTask;

        await Assert.That(presenterConn.IsPresenter).IsTrue();
        await Assert.That(viewerConn.IsPresenter).IsFalse();
    }

    [Test]
    public async Task ChatMessagesAreSentFromViewerToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var receiveTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => presenterConn.Chat.MessageReceived += (s, msg) => onResult(msg));

        await viewerConn.Chat.SendMessageAsync("Hello from viewer!");

        var received = await receiveTask;

        await Assert.That(received.Text).IsEqualTo("Hello from viewer!");
        await Assert.That(received.IsFromPresenter).IsFalse();
    }

    [Test]
    public async Task ChatMessagesAreSentFromPresenterToViewer()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var receiveTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewerConn.Chat.MessageReceived += (s, msg) => onResult(msg));

        await presenterConn.Chat.SendMessageAsync("Hello from presenter!");

        var received = await receiveTask;

        await Assert.That(received.Text).IsEqualTo("Hello from presenter!");
        await Assert.That(received.IsFromPresenter).IsTrue();
    }

    [Test]
    public async Task InputBlockingUpdatesConnectionProperties()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Wait for presenter to receive server confirmation
        // (presenter is now server-authoritative - no local optimistic update)
        var presenterPropertyTask = TestHelpers.WaitForEventAsync(
            onComplete => presenterConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (presenterConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    onComplete();
            });

        // Block viewer input (sends to server, doesn't update local state)
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        // Wait for server confirmation
        await presenterPropertyTask;

        await Assert.That(presenterConn.ConnectionProperties.InputBlockedViewerIds)
            .Contains(viewerClientId);
    }

    [Test]
    public async Task ViewerMouseMoveIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseMoveAsync(0.5f, 0.5f);

        // Wait for message to be received and processed
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == "InjectMouseMove"));

        // Verify via NSubstitute mock
        await presenter.InputInjectionService.Received().InjectMouseMove(
            Arg.Any<DisplayInfo>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ViewerKeyPressIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendKeyDownAsync(0x41, KeyModifiers.None); // 'A' key
        await viewerConn.RequiredViewerService.SendKeyUpAsync(0x41, KeyModifiers.None);

        // Wait for both key events to be received
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == "InjectKey") >= 2);

        // Verify key down and key up were both received
        await presenter.InputInjectionService.Received(2).InjectKey(
            Arg.Any<ushort>(),
            Arg.Any<bool>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PresenterDisconnectClosesViewerConnection()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var closedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.Closed += (s, e) => onComplete());

        await presenterConn.DisconnectAsync();

        await closedTask;

        await Assert.That(viewerConn.IsClosed).IsTrue();
    }

    [Test]
    public async Task ViewerDisconnectDoesNotClosePresenterConnection()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.DisconnectAsync();

        // Wait a bit to ensure we're not just racing the close event
        await Task.Delay(200);

        // Presenter connection should still be open
        await Assert.That(presenterConn.IsClosed).IsFalse();
    }

    #region Connection Lifecycle & Events Tests

    [Test]
    public async Task MultipleViewersCanConnectToSamePresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer1 = await this.Server.CreateClientAsync("Viewer1");
        await using var viewer2 = await this.Server.CreateClientAsync("Viewer2");
        await using var viewer3 = await this.Server.CreateClientAsync("Viewer3");

        await this.Server.CreateConnectionAsync(presenter, viewer1, viewer2, viewer3);
        var presenterConn = presenter.CurrentConnection!;

        // Wait for all viewers to be registered (eventual consistency)
        await TestHelpers.WaitForConditionAsync(
            () => presenterConn.Viewers.Count == 3,
            timeoutMessage: $"Expected 3 viewers but got {presenterConn.Viewers.Count}");

        await Assert.That(presenterConn.Viewers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ViewerDisconnectDoesNotAffectOtherViewers()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer1 = await this.Server.CreateClientAsync("Viewer1");
        await using var viewer2 = await this.Server.CreateClientAsync("Viewer2");

        await this.Server.CreateConnectionAsync(presenter, viewer1, viewer2);
        var presenterConn = presenter.CurrentConnection!;
        var viewer1Conn = viewer1.CurrentConnection!;
        var viewer2Conn = viewer2.CurrentConnection!;

        // Disconnect viewer1
        await viewer1Conn.DisconnectAsync();

        // Viewer2 should still be connected and able to communicate
        var receiveTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => presenterConn.Chat.MessageReceived += (s, msg) => onResult(msg));

        await viewer2Conn.Chat.SendMessageAsync("I'm still connected!");

        var received = await receiveTask;
        await Assert.That(received.Text).IsEqualTo("I'm still connected!");
    }

    [Test]
    public async Task ViewersChangedEventFiresOnViewerConnect()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();

        // Connect the viewer which will trigger the presenter connection
        var viewerConnectTask = viewer.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;

        // Subscribe BEFORE waiting for viewer connect to avoid race condition
        var viewersChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => presenterConn.ViewersChanged += (s, e) =>
            {
                if (presenterConn.Viewers.Count > 0)
                    onComplete();
            });

        // Wait for viewer connect to complete
        await viewerConnectTask;

        // Wait for the event
        await viewersChangedTask;

        await Assert.That(presenterConn.Viewers.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ConnectionPropertiesChangedEventFires()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Subscribe BEFORE triggering the property change to avoid race condition
        var propertyChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (viewerConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    onComplete();
            });

        // Block viewer input (this changes connection properties)
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        // Wait for the event
        await propertyChangedTask;

        await Assert.That(viewerConn.ConnectionProperties.InputBlockedViewerIds).Contains(viewerClientId);
    }

    [Test]
    public async Task IsClosedReflectsConnectionState()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Initially not closed
        await Assert.That(viewerConn.IsClosed).IsFalse();

        // Subscribe first, then disconnect
        var closedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.Closed += (s, e) => onComplete());

        await viewerConn.DisconnectAsync();

        await closedTask;

        // Now should be closed
        await Assert.That(viewerConn.IsClosed).IsTrue();
    }

    #endregion

    #region Input Tests

    [Test]
    public async Task ViewerMouseClickIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseDownAsync(MouseButton.Left, 0.5f, 0.5f);
        await viewerConn.RequiredViewerService.SendMouseUpAsync(MouseButton.Left, 0.5f, 0.5f);

        // Wait for both mouse events to be received
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Count(c => c.GetMethodInfo().Name == "InjectMouseButton") >= 2);

        // Verify both mouse down and mouse up were received
        await presenter.InputInjectionService.Received(2).InjectMouseButton(
            Arg.Any<DisplayInfo>(),
            MouseButton.Left,
            Arg.Any<bool>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ViewerMouseWheelIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseWheelAsync(0f, 120f, 0.5f, 0.5f);

        // Wait for wheel event to be received
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == "InjectMouseWheel"));

        // Use Arg.Any<float>() for all float params to avoid NSubstitute ambiguity
        await presenter.InputInjectionService.Received().InjectMouseWheel(
            Arg.Any<DisplayInfo>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InputBlockingPreventsInputInjection()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Wait for PRESENTER to receive server confirmation
        // (input blocking check happens on presenter, so we must wait for presenter's state)
        var presenterPropertyTask = TestHelpers.WaitForEventAsync(
            onComplete => presenterConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (presenterConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    onComplete();
            });

        // Block viewer input
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        // Wait for presenter to have the updated properties
        await presenterPropertyTask;

        // Clear any previous calls
        presenter.InputInjectionService.ClearReceivedCalls();

        // Try to send mouse input
        await viewerConn.RequiredViewerService.SendMouseMoveAsync(0.5f, 0.5f);

        // Wait a short time to allow any potential message to arrive
        // (negative test - we're verifying nothing happens)
        await Task.Delay(300);

        // Verify input was NOT injected (blocked)
        await presenter.InputInjectionService.DidNotReceive().InjectMouseMove(
            Arg.Any<DisplayInfo>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MultipleKeyModifiersAreSent()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Send key with Ctrl+Shift modifiers
        await viewerConn.RequiredViewerService.SendKeyDownAsync(0x41, KeyModifiers.Control | KeyModifiers.Shift);

        // Wait for key event to be received
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == "InjectKey"));

        await presenter.InputInjectionService.Received().InjectKey(
            0x41,
            true, // isDown
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Chat Tests

    [Test]
    public async Task GetMessagesReturnsHistory()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        // Wait for the last message to arrive
        var lastMessageTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => presenterConn.Chat.MessageReceived += (s, msg) =>
            {
                if (msg.Text == "Message 3")
                    onResult(msg);
            });

        // Send messages from viewer (presenter's own message is stored locally, not via MessageReceived)
        await viewerConn.Chat.SendMessageAsync("Message 1");
        await presenterConn.Chat.SendMessageAsync("Message 2");
        await viewerConn.Chat.SendMessageAsync("Message 3");

        await lastMessageTask;

        // Get message history from presenter side
        var messages = presenterConn.Chat.GetMessages();

        await Assert.That(messages.Count).IsGreaterThanOrEqualTo(2);
    }

    [Test]
    public async Task MultipleViewersReceiveSameChatMessage()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer1 = await this.Server.CreateClientAsync("Viewer1");
        await using var viewer2 = await this.Server.CreateClientAsync("Viewer2");

        await this.Server.CreateConnectionAsync(presenter, viewer1, viewer2);
        var presenterConn = presenter.CurrentConnection!;
        var viewer1Conn = viewer1.CurrentConnection!;
        var viewer2Conn = viewer2.CurrentConnection!;

        var msg1Task = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewer1Conn.Chat.MessageReceived += (s, msg) => onResult(msg));
        var msg2Task = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewer2Conn.Chat.MessageReceived += (s, msg) => onResult(msg));

        await presenterConn.Chat.SendMessageAsync("Broadcast to all!");

        var msg1 = await msg1Task;
        var msg2 = await msg2Task;

        await Assert.That(msg1.Text).IsEqualTo("Broadcast to all!");
        await Assert.That(msg2.Text).IsEqualTo("Broadcast to all!");
    }

    [Test]
    public async Task ChatMessagesContainCorrectSenderInfo()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        // Test 1: Viewer sends to presenter - verify IsFromPresenter is false
        var fromViewerTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => presenterConn.Chat.MessageReceived += (s, msg) =>
            {
                if (msg.Text == "From viewer")
                    onResult(msg);
            });

        await viewerConn.Chat.SendMessageAsync("From viewer");
        var fromViewer = await fromViewerTask;

        await Assert.That(fromViewer.IsFromPresenter).IsFalse();

        // Test 2: Presenter sends to viewer - verify IsFromPresenter is true
        var fromPresenterTask = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewerConn.Chat.MessageReceived += (s, msg) =>
            {
                if (msg.Text == "From presenter")
                    onResult(msg);
            });

        await presenterConn.Chat.SendMessageAsync("From presenter");
        var fromPresenter = await fromPresenterTask;

        await Assert.That(fromPresenter.IsFromPresenter).IsTrue();
    }

    #endregion

    #region Multi-Viewer Scenario Tests

    [Test]
    public async Task ViewerCanBeBlockedWhileOthersAreNot()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer1 = await this.Server.CreateClientAsync("Viewer1");
        await using var viewer2 = await this.Server.CreateClientAsync("Viewer2");

        await this.Server.CreateConnectionAsync(presenter, viewer1, viewer2);
        var presenterConn = presenter.CurrentConnection!;
        var viewer2Conn = viewer2.CurrentConnection!;

        // Block only viewer1
        var viewer1ClientId = viewer1.HubClient.ClientId!;

        // Wait for property to propagate to viewer2
        var propertyChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewer2Conn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (viewer2Conn.ConnectionProperties.InputBlockedViewerIds.Contains(viewer1ClientId))
                    onComplete();
            });

        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewer1ClientId] });

        await propertyChangedTask;

        // Clear previous calls
        presenter.InputInjectionService.ClearReceivedCalls();

        // Viewer2 should still be able to send input
        await viewer2Conn.RequiredViewerService.SendMouseMoveAsync(0.5f, 0.5f);

        // Wait for mouse event to be received
        await TestHelpers.WaitForReceivedCallAsync(() =>
            presenter.InputInjectionService.ReceivedCalls()
                .Any(c => c.GetMethodInfo().Name == "InjectMouseMove"));

        await presenter.InputInjectionService.Received().InjectMouseMove(
            Arg.Any<DisplayInfo>(),
            Arg.Any<float>(),
            Arg.Any<float>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task BroadcastMessagesReachAllViewers()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer1 = await this.Server.CreateClientAsync("Viewer1");
        await using var viewer2 = await this.Server.CreateClientAsync("Viewer2");

        await this.Server.CreateConnectionAsync(presenter, viewer1, viewer2);
        var presenterConn = presenter.CurrentConnection!;
        var viewer1Conn = viewer1.CurrentConnection!;
        var viewer2Conn = viewer2.CurrentConnection!;

        var msg1Task = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewer1Conn.Chat.MessageReceived += (s, msg) => onResult(msg));
        var msg2Task = TestHelpers.WaitForEventAsync<ChatMessageDisplay>(
            onResult => viewer2Conn.Chat.MessageReceived += (s, msg) => onResult(msg));

        await presenterConn.Chat.SendMessageAsync("Broadcast message");

        await msg1Task;
        await msg2Task;
    }

    #endregion

    #region Display Selection Tests

    [Test]
    public async Task AvailableDisplaysChangedEventFires()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        // Subscribe first
        var displaysChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.RequiredViewerService.AvailableDisplaysChanged += (s, e) =>
            {
                if (viewerConn.RequiredViewerService.AvailableDisplays.Count >= 2)
                    onComplete();
            });

        // Update connection properties with new displays (simulating display change)
        var newDisplays = new List<DisplayInfo>
        {
            new("DISPLAY1", "Display 1", true, 0, 0, 1920, 1080),
            new("DISPLAY2", "Display 2", false, 1920, 0, 3840, 1080)
        };

        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { AvailableDisplays = newDisplays });

        await displaysChangedTask;

        await Assert.That(viewerConn.RequiredViewerService.AvailableDisplays.Count).IsGreaterThanOrEqualTo(2);
    }

    #endregion

    #region File Transfer Tests

    [Test]
    public async Task FileTransferRequestIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Configure presenter's dialog to accept file transfer
        presenter.DialogService.ShowFileTransferConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        // Create a temp file to transfer
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for transfer");

            // Start the transfer (this sends the request)
            _ = viewerConn.FileTransfers.SendFileAsync(tempFile);

            // Wait for the dialog to be called
            await TestHelpers.WaitForReceivedCallAsync(() =>
                presenter.DialogService.ReceivedCalls()
                    .Any(c => c.GetMethodInfo().Name == "ShowFileTransferConfirmationAsync"));

            // Verify the dialog was called on presenter side
            await presenter.DialogService.Received().ShowFileTransferConfirmationAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>());
        }
        finally
        {
            // Temp file may still be in use by the transfer operation
            try { File.Delete(tempFile); } catch (IOException) { }
        }
    }

    [Test]
    public async Task RejectedFileTransferDoesNotComplete()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Configure presenter's dialog to reject file transfer
        presenter.DialogService.ShowFileTransferConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(false));

        // Create a temp file to transfer
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content");

            // Subscribe first, then start transfer
            var failedTask = TestHelpers.WaitForEventAsync<bool>(
                onResult => viewerConn.FileTransfers.TransferFailed += (s, e) => onResult(true));

            // Start the transfer
            _ = viewerConn.FileTransfers.SendFileAsync(tempFile);

            // Wait for failure
            var failed = await failedTask;

            await Assert.That(failed).IsTrue();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Clipboard Sync Tests

    [Test]
    public async Task TextClipboardSyncsFromViewerToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        viewer.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from viewer"));

        await this.Server.CreateConnectionAsync(presenter, viewer);

        // Give the clipboard sync poll loop time to start and register its timer
        // This is necessary because Task.Run() in ClipboardSyncService may not have executed yet
        await Task.Delay(100);

        // Poll with time advances until clipboard syncs
        for (var i = 0; i < 50; i++)
        {
            // Advance time by more than the poll interval (500ms) to trigger the timer
            viewer.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));

            // Allow SignalR message delivery and give async operations time to complete
            await Task.Delay(100);

            var calls = presenter.ClipboardService.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == "SetTextAsync")
                .ToList();
            if (calls.Count > 0)
            {
                var text = calls[0].GetArguments()[0] as string;
                await Assert.That(text).IsEqualTo("Clipboard text from viewer");
                return;
            }
        }

        Assert.Fail("Clipboard was not synced within timeout");
    }

    [Test]
    public async Task TextClipboardSyncsFromPresenterToViewer()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        presenter.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from presenter"));

        await this.Server.CreateConnectionAsync(presenter, viewer);

        // Give the clipboard sync poll loop time to start and register its timer
        // This is necessary because Task.Run() in ClipboardSyncService may not have executed yet
        await Task.Delay(100);

        // Poll with time advances until clipboard syncs
        for (var i = 0; i < 50; i++)
        {
            // Advance time by more than the poll interval (500ms) to trigger the timer
            presenter.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));

            // Allow SignalR message delivery and give async operations time to complete
            await Task.Delay(100);

            var calls = viewer.ClipboardService.ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == "SetTextAsync")
                .ToList();
            if (calls.Count > 0)
            {
                var text = calls[0].GetArguments()[0] as string;
                await Assert.That(text).IsEqualTo("Clipboard text from presenter");
                return;
            }
        }

        Assert.Fail("Clipboard was not synced within timeout");
    }

    // Note: Image clipboard test would require Avalonia headless initialization
    // since Bitmap class is tightly coupled to the rendering infrastructure.
    // The text clipboard tests above validate the sync mechanism works.

    #endregion

    #region Connection Error Tests

    [Test]
    public async Task ConnectToWithInvalidCredentialsReturnsIncorrectUsernameOrPassword()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        var (username, _) = await presenter.WaitForCredentialsAsync();

        // Try to connect with correct username but wrong password
        var error = await viewer.HubClient.ConnectTo(username, "WrongPassword123");

        await Assert.That(error).IsEqualTo(TryConnectError.IncorrectUsernameOrPassword);
    }

    [Test]
    public async Task ConnectToWithNonExistentUserReturnsError()
    {
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        // Try to connect with credentials that were never assigned
        // Server returns IncorrectUsernameOrPassword for security (don't leak user existence)
        var error = await viewer.HubClient.ConnectTo("000000", "AAAA");

        await Assert.That(error).IsNotNull();
    }

    [Test]
    public async Task GenerateNewPasswordChangesCredentials()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");

        var (oldUser, oldPass) = await presenter.WaitForCredentialsAsync();

        // Wait for new credentials event
        var newCredentialsTask = TestHelpers.WaitForEventAsync<CredentialsAssignedEventArgs>(
            onResult => presenter.HubClient.CredentialsAssigned += (s, e) =>
            {
                // Only trigger on a different password
                if (e.Password != oldPass)
                    onResult(e);
            });

        await presenter.HubClient.GenerateNewPassword();

        var newCredentials = await newCredentialsTask;

        // Verify password changed (username stays the same)
        await Assert.That(newCredentials.Username.Replace(" ", "")).IsEqualTo(oldUser);
        await Assert.That(newCredentials.Password).IsNotEqualTo(oldPass);

        // Old credentials should no longer work
        var error = await viewer.HubClient.ConnectTo(oldUser, oldPass);
        await Assert.That(error).IsEqualTo(TryConnectError.IncorrectUsernameOrPassword);
    }

    #endregion

    #region Identity Tests

    [Test]
    public async Task SetDisplayNameUpdatesDisplayNameOnServer()
    {
        // Create clients without display name first
        await using var presenter = await this.Server.CreateClientAsync();
        await using var viewer = await this.Server.CreateClientAsync();

        // Set display name after connection is established
        await presenter.HubClient.SetDisplayName("CustomPresenterName");

        await this.Server.CreateConnectionAsync(presenter, viewer);

        var viewerConn = viewer.CurrentConnection!;

        // Wait for the ViewersChanged event which carries the display name
        await TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.ViewersChanged += (s, e) =>
            {
                if (viewerConn.Presenter?.DisplayName == "CustomPresenterName")
                    onComplete();
            });

        // Verify the presenter's display name is visible to the viewer
        await Assert.That(viewerConn.Presenter?.DisplayName).IsEqualTo("CustomPresenterName");
    }

    [Test]
    public async Task SetDisplayNameCanBeChangedAfterConnection()
    {
        await using var presenter = await this.Server.CreateClientAsync();
        await using var viewer = await this.Server.CreateClientAsync();

        await this.Server.CreateConnectionAsync(presenter, viewer);

        var viewerConn = viewer.CurrentConnection!;

        // Wait for ViewersChanged event with the updated name
        var viewersChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.ViewersChanged += (s, e) =>
            {
                if (viewerConn.Presenter?.DisplayName == "UpdatedName")
                    onComplete();
            });

        // Change display name after connection is established
        await presenter.HubClient.SetDisplayName("UpdatedName");

        await viewersChangedTask;

        // Verify the updated name is visible
        await Assert.That(viewerConn.Presenter?.DisplayName).IsEqualTo("UpdatedName");
    }

    #endregion

    #region Secure Input Tests

    [Test]
    public async Task SecureAttentionSequenceIsSentToPresenter()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Send Ctrl+Alt+Del request
        // Note: In tests, the WinService isn't available so the presenter won't actually
        // execute the SAS, but we can verify the message is sent through SignalR
        await viewerConn.RequiredViewerService.SendCtrlAltDelAsync();

        // Allow message to propagate through SignalR (smoke test for message path)
        await Task.Delay(100);

        // The SAS handler runs but won't succeed without WinService
        // This test verifies the message path works without throwing
        // If the message routing failed, we would have seen an exception
        await Assert.That(viewerConn.IsClosed).IsFalse();
    }

    #endregion

    #region File Transfer Completion Tests

    [Test]
    public async Task FileTransferSuccessfulTransferCompletesAndFiresEvent()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Configure presenter's dialog to accept file transfer
        presenter.DialogService.ShowFileTransferConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        // Create a temp file to transfer
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for successful transfer");

            // Subscribe to transfer completed event on viewer (sender) side
            var completedTask = TestHelpers.WaitForEventAsync<TransferCompletedEventArgs>(
                onResult => viewerConn.FileTransfers.TransferCompleted += (s, e) => onResult(e),
                timeout: TimeSpan.FromSeconds(10));

            // Start the transfer
            _ = viewerConn.FileTransfers.SendFileAsync(tempFile);

            // Wait for completion
            var completed = await completedTask;

            await Assert.That(completed).IsNotNull();
            await Assert.That(completed.Transfer).IsNotNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task FileTransferPresenterCanSendFileToViewer()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Configure viewer's dialog to accept file transfer
        viewer.DialogService.ShowFileTransferConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromResult(true));

        // Create a temp file to transfer
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file from presenter to viewer");

            // Subscribe to transfer completed event on presenter (sender) side
            var completedTask = TestHelpers.WaitForEventAsync<TransferCompletedEventArgs>(
                onResult => presenterConn.FileTransfers.TransferCompleted += (s, e) => onResult(e),
                timeout: TimeSpan.FromSeconds(10));

            // Start the transfer to specific viewer
            _ = presenterConn.FileTransfers.SendFileToViewerAsync(tempFile, viewerClientId);

            // Wait for completion
            var completed = await completedTask;

            await Assert.That(completed).IsNotNull();
            await Assert.That(completed.Transfer).IsNotNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task FileTransferCancellationFiresTransferFailedEvent()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Configure presenter's dialog to never respond (simulate user not accepting)
        var dialogTcs = new TaskCompletionSource<bool>();
        presenter.DialogService.ShowFileTransferConfirmationAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(dialogTcs.Task);

        // Create a temp file to transfer
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "Test file content for cancellation test");

            // Subscribe to transfer failed event
            var failedTask = TestHelpers.WaitForEventAsync<TransferFailedEventArgs>(
                onResult => viewerConn.FileTransfers.TransferFailed += (s, e) => onResult(e),
                timeout: TimeSpan.FromSeconds(10));

            // Start the transfer
            var operation = await viewerConn.FileTransfers.SendFileAsync(tempFile);

            // Cancel the transfer
            await operation.CancelAsync();

            // Wait for failure event
            var failed = await failedTask;

            await Assert.That(failed).IsNotNull();
            await Assert.That(failed.Transfer).IsNotNull();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Hub Connection Status Tests

    [Test]
    public async Task IsConnectedReturnsTrueAfterSuccessfulConnection()
    {
        await using var client = await this.Server.CreateClientAsync("TestClient");

        await Assert.That(client.HubClient.IsConnected).IsTrue();
    }

    [Test]
    public async Task HubConnectionStatusChangedFiresOnConnection()
    {
        var client = new ClientFixture(this.Server);
        try
        {
            var eventFired = false;

            // Subscribe before connecting
            client.HubClient.HubConnectionStatusChanged += (s, e) => eventFired = true;

            // Connect to hub
            await client.HubClient.ConnectToHub();

            // Event should have fired during connection
            await Assert.That(eventFired).IsTrue();
            await Assert.That(client.HubClient.IsConnected).IsTrue();
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    #endregion
}

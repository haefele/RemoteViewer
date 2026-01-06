using NSubstitute;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.IntegrationTests.Fixtures;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.IntegrationTests;

[ClassDataSource<ServerFixture>(Shared = SharedType.PerAssembly)]
public class ConnectionHubClientTests(ServerFixture serverFixture)
{
    [Test]
    public async Task TwoClientsCanEstablishConnection()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Wait for viewer to appear in connection
        await Task.Delay(100);

        // Block viewer input
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        // Wait for property propagation
        await Task.Delay(100);

        await Assert.That(presenterConn.ConnectionProperties.InputBlockedViewerIds)
            .Contains(viewerClientId);
    }

    [Test]
    public async Task ViewerMouseMoveIsSentToPresenter()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseMoveAsync(0.5f, 0.5f);

        // Give time for message to be received and processed
        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendKeyDownAsync(0x41, KeyModifiers.None); // 'A' key
        await viewerConn.RequiredViewerService.SendKeyUpAsync(0x41, KeyModifiers.None);

        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer1 = await serverFixture.CreateClientAsync("Viewer1");
        await using var viewer2 = await serverFixture.CreateClientAsync("Viewer2");
        await using var viewer3 = await serverFixture.CreateClientAsync("Viewer3");

        await serverFixture.CreateConnectionAsync(presenter, viewer1, viewer2, viewer3);
        var presenterConn = presenter.CurrentConnection!;

        await Assert.That(presenterConn.Viewers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ViewerDisconnectDoesNotAffectOtherViewers()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer1 = await serverFixture.CreateClientAsync("Viewer1");
        await using var viewer2 = await serverFixture.CreateClientAsync("Viewer2");

        await serverFixture.CreateConnectionAsync(presenter, viewer1, viewer2);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();

        // Connect the viewer which will trigger the presenter connection
        var viewerConnectTask = viewer.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;

        // Wait for viewer connect to complete
        await viewerConnectTask;

        await TestHelpers.WaitForEventAsync(
            onComplete => presenterConn.ViewersChanged += (s, e) =>
            {
                if (presenterConn.Viewers.Count > 0)
                    onComplete();
            });

        await Assert.That(presenterConn.Viewers.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ConnectionPropertiesChangedEventFires()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Block viewer input (this changes connection properties)
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        await TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (viewerConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    onComplete();
            });

        await Assert.That(viewerConn.ConnectionProperties.InputBlockedViewerIds).Contains(viewerClientId);
    }

    [Test]
    public async Task IsClosedReflectsConnectionState()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseDownAsync(MouseButton.Left, 0.5f, 0.5f);
        await viewerConn.RequiredViewerService.SendMouseUpAsync(MouseButton.Left, 0.5f, 0.5f);

        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        await viewerConn.RequiredViewerService.SendMouseWheelAsync(0f, 120f, 0.5f, 0.5f);

        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;

        var viewerClientId = viewer.HubClient.ClientId!;

        // Wait for property to propagate to viewer
        var propertyChangedTask = TestHelpers.WaitForEventAsync(
            onComplete => viewerConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (viewerConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    onComplete();
            });

        // Block viewer input
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewerClientId] });

        await propertyChangedTask;

        // Clear any previous calls
        presenter.InputInjectionService.ClearReceivedCalls();

        // Try to send mouse input
        await viewerConn.RequiredViewerService.SendMouseMoveAsync(0.5f, 0.5f);

        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
        var viewerConn = viewer.CurrentConnection!;

        // Send key with Ctrl+Shift modifiers
        await viewerConn.RequiredViewerService.SendKeyDownAsync(0x41, KeyModifiers.Control | KeyModifiers.Shift);

        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer1 = await serverFixture.CreateClientAsync("Viewer1");
        await using var viewer2 = await serverFixture.CreateClientAsync("Viewer2");

        await serverFixture.CreateConnectionAsync(presenter, viewer1, viewer2);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer1 = await serverFixture.CreateClientAsync("Viewer1");
        await using var viewer2 = await serverFixture.CreateClientAsync("Viewer2");

        await serverFixture.CreateConnectionAsync(presenter, viewer1, viewer2);
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
        await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer1 = await serverFixture.CreateClientAsync("Viewer1");
        await using var viewer2 = await serverFixture.CreateClientAsync("Viewer2");

        await serverFixture.CreateConnectionAsync(presenter, viewer1, viewer2);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
            props with { AvailableDisplays = newDisplays }, forceSend: true);

        await displaysChangedTask;

        await Assert.That(viewerConn.RequiredViewerService.AvailableDisplays.Count).IsGreaterThanOrEqualTo(2);
    }

    #endregion

    #region File Transfer Tests

    [Test]
    public async Task FileTransferRequestIsSentToPresenter()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
            var sendTask = viewerConn.FileTransfers.SendFileAsync(tempFile);

            // Give time for the request to be received
            await Task.Delay(500);

            // Verify the dialog was called on presenter side
            await presenter.DialogService.Received().ShowFileTransferConfirmationAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>());
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task RejectedFileTransferDoesNotComplete()
    {
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");
        await serverFixture.CreateConnectionAsync(presenter, viewer);
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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");

        viewer.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from viewer"));

        await serverFixture.CreateConnectionAsync(presenter, viewer);

        // Poll with time advances until clipboard syncs or timeout
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            viewer.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(200);

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
        await using var presenter = await serverFixture.CreateClientAsync("Presenter");
        await using var viewer = await serverFixture.CreateClientAsync("Viewer");

        presenter.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from presenter"));

        await serverFixture.CreateConnectionAsync(presenter, viewer);

        // Poll with time advances until clipboard syncs or timeout
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
            presenter.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));
            await Task.Delay(200);

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

    #endregion
}

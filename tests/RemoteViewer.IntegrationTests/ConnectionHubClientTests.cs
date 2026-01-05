using NSubstitute;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.IntegrationTests.Fixtures;
using RemoteViewer.Server.Tests;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.IntegrationTests;

[ClassDataSource<ServerFixture>(Shared = SharedType.PerAssembly)]
public class ConnectionHubClientTests(ServerFixture serverFixture)
{
    private async Task<(ClientFixture Presenter, ClientFixture Viewer, Connection PresenterConn, Connection ViewerConn)> SetupConnectionAsync()
    {
        var presenter = new ClientFixture(serverFixture, "Presenter");
        var viewer = new ClientFixture(serverFixture, "Viewer");

        await presenter.HubClient.ConnectToHub();
        await viewer.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewerConnTask = viewer.WaitForConnectionAsync();

        var error = await viewer.HubClient.ConnectTo(username, password);
        if (error != null)
            throw new InvalidOperationException($"Connection failed: {error}");

        var presenterConn = await presenterConnTask;
        var viewerConn = await viewerConnTask;

        return (presenter, viewer, presenterConn, viewerConn);
    }

    [Test]
    public async Task TwoClientsCanEstablishConnection()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer = new ClientFixture(serverFixture, "Viewer");

        await presenter.HubClient.ConnectToHub();
        await viewer.HubClient.ConnectToHub();

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
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var receivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
            presenterConn.Chat.MessageReceived += (s, msg) => receivedTcs.TrySetResult(msg);

            await viewerConn.Chat.SendMessageAsync("Hello from viewer!");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => receivedTcs.TrySetCanceled());
            var received = await receivedTcs.Task;

            await Assert.That(received.Text).IsEqualTo("Hello from viewer!");
            await Assert.That(received.IsFromPresenter).IsFalse();
        }
    }

    [Test]
    public async Task ChatMessagesAreSentFromPresenterToViewer()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var receivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
            viewerConn.Chat.MessageReceived += (s, msg) => receivedTcs.TrySetResult(msg);

            await presenterConn.Chat.SendMessageAsync("Hello from presenter!");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => receivedTcs.TrySetCanceled());
            var received = await receivedTcs.Task;

            await Assert.That(received.Text).IsEqualTo("Hello from presenter!");
            await Assert.That(received.IsFromPresenter).IsTrue();
        }
    }

    [Test]
    public async Task InputBlockingUpdatesConnectionProperties()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task ViewerMouseMoveIsSentToPresenter()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task ViewerKeyPressIsSentToPresenter()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task PresenterDisconnectClosesViewerConnection()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var closedTcs = new TaskCompletionSource();
            viewerConn.Closed += (s, e) => closedTcs.TrySetResult();

            await presenterConn.DisconnectAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => closedTcs.TrySetCanceled());
            await closedTcs.Task;

            await Assert.That(viewerConn.IsClosed).IsTrue();
        }
    }

    [Test]
    public async Task ViewerDisconnectDoesNotClosePresenterConnection()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            await viewerConn.DisconnectAsync();

            // Wait a bit to ensure we're not just racing the close event
            await Task.Delay(200);

            // Presenter connection should still be open
            await Assert.That(presenterConn.IsClosed).IsFalse();
        }
    }

    #region Connection Lifecycle & Events Tests

    [Test]
    public async Task MultipleViewersCanConnectToSamePresenter()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer1 = new ClientFixture(serverFixture, "Viewer1");
        await using var viewer2 = new ClientFixture(serverFixture, "Viewer2");
        await using var viewer3 = new ClientFixture(serverFixture, "Viewer3");

        await presenter.HubClient.ConnectToHub();
        await viewer1.HubClient.ConnectToHub();
        await viewer2.HubClient.ConnectToHub();
        await viewer3.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();

        // Connect all three viewers
        var error1 = await viewer1.HubClient.ConnectTo(username, password);
        var error2 = await viewer2.HubClient.ConnectTo(username, password);
        var error3 = await viewer3.HubClient.ConnectTo(username, password);

        await Assert.That(error1).IsNull();
        await Assert.That(error2).IsNull();
        await Assert.That(error3).IsNull();

        var presenterConn = await presenterConnTask;

        // Give time for all viewers to be registered
        await Task.Delay(200);

        await Assert.That(presenterConn.Viewers.Count).IsEqualTo(3);
    }

    [Test]
    public async Task ViewerDisconnectDoesNotAffectOtherViewers()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer1 = new ClientFixture(serverFixture, "Viewer1");
        await using var viewer2 = new ClientFixture(serverFixture, "Viewer2");

        await presenter.HubClient.ConnectToHub();
        await viewer1.HubClient.ConnectToHub();
        await viewer2.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewer1ConnTask = viewer1.WaitForConnectionAsync();
        var viewer2ConnTask = viewer2.WaitForConnectionAsync();

        await viewer1.HubClient.ConnectTo(username, password);
        await viewer2.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;
        var viewer1Conn = await viewer1ConnTask;
        var viewer2Conn = await viewer2ConnTask;

        // Give time for all viewers to be registered
        await Task.Delay(200);

        // Disconnect viewer1
        await viewer1Conn.DisconnectAsync();
        await Task.Delay(200);

        // Viewer2 should still be connected and able to communicate
        var receivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
        presenterConn.Chat.MessageReceived += (s, msg) => receivedTcs.TrySetResult(msg);

        await viewer2Conn.Chat.SendMessageAsync("I'm still connected!");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => receivedTcs.TrySetCanceled());
        var received = await receivedTcs.Task;

        await Assert.That(received.Text).IsEqualTo("I'm still connected!");
    }

    [Test]
    public async Task ViewersChangedEventFiresOnViewerConnect()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer = new ClientFixture(serverFixture, "Viewer");

        await presenter.HubClient.ConnectToHub();
        await viewer.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        // Set up to wait for both connection and viewers changed event
        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewersChangedTcs = new TaskCompletionSource();

        // Connect the viewer which will trigger the presenter connection
        var viewerConnectTask = viewer.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;

        // Subscribe to event after getting connection
        presenterConn.ViewersChanged += (s, e) =>
        {
            if (presenterConn.Viewers.Count > 0)
                viewersChangedTcs.TrySetResult();
        };

        // Wait for viewer connect to complete
        await viewerConnectTask;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => viewersChangedTcs.TrySetCanceled());
        await viewersChangedTcs.Task;

        await Assert.That(presenterConn.Viewers.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task ConnectionPropertiesChangedEventFires()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var viewerClientId = viewer.HubClient.ClientId!;

            var propsChangedTcs = new TaskCompletionSource();
            viewerConn.ConnectionPropertiesChanged += (s, e) =>
            {
                if (viewerConn.ConnectionProperties.InputBlockedViewerIds.Contains(viewerClientId))
                    propsChangedTcs.TrySetResult();
            };

            // Block viewer input (this changes connection properties)
            await presenterConn.UpdateConnectionPropertiesAndSend(props =>
                props with { InputBlockedViewerIds = [viewerClientId] });

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => propsChangedTcs.TrySetCanceled());
            await propsChangedTcs.Task;

            await Assert.That(viewerConn.ConnectionProperties.InputBlockedViewerIds).Contains(viewerClientId);
        }
    }

    [Test]
    public async Task IsClosedReflectsConnectionState()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            // Initially not closed
            await Assert.That(viewerConn.IsClosed).IsFalse();

            // Set up to wait for close event
            var closedTcs = new TaskCompletionSource();
            viewerConn.Closed += (s, e) => closedTcs.TrySetResult();

            // Disconnect
            await viewerConn.DisconnectAsync();

            // Wait for close event
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => closedTcs.TrySetCanceled());
            await closedTcs.Task;

            // Now should be closed
            await Assert.That(viewerConn.IsClosed).IsTrue();
        }
    }

    #endregion

    #region Input Tests

    [Test]
    public async Task ViewerMouseClickIsSentToPresenter()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task ViewerMouseWheelIsSentToPresenter()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task InputBlockingPreventsInputInjection()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var viewerClientId = viewer.HubClient.ClientId!;

            // Block viewer input
            await presenterConn.UpdateConnectionPropertiesAndSend(props =>
                props with { InputBlockedViewerIds = [viewerClientId] });

            // Wait for property propagation
            await Task.Delay(200);

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
    }

    [Test]
    public async Task MultipleKeyModifiersAreSent()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            // Send key with Ctrl+Shift modifiers
            await viewerConn.RequiredViewerService.SendKeyDownAsync(0x41, KeyModifiers.Control | KeyModifiers.Shift);

            await Task.Delay(200);

            await presenter.InputInjectionService.Received().InjectKey(
                0x41,
                true, // isDown
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>());
        }
    }

    #endregion

    #region Chat Tests

    [Test]
    public async Task GetMessagesReturnsHistory()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            // Send a few messages
            await viewerConn.Chat.SendMessageAsync("Message 1");
            await Task.Delay(100);
            await presenterConn.Chat.SendMessageAsync("Message 2");
            await Task.Delay(100);
            await viewerConn.Chat.SendMessageAsync("Message 3");
            await Task.Delay(200);

            // Get message history from presenter side
            var messages = presenterConn.Chat.GetMessages();

            await Assert.That(messages.Count).IsGreaterThanOrEqualTo(3);
        }
    }

    [Test]
    public async Task MultipleViewersReceiveSameChatMessage()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer1 = new ClientFixture(serverFixture, "Viewer1");
        await using var viewer2 = new ClientFixture(serverFixture, "Viewer2");

        await presenter.HubClient.ConnectToHub();
        await viewer1.HubClient.ConnectToHub();
        await viewer2.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewer1ConnTask = viewer1.WaitForConnectionAsync();
        var viewer2ConnTask = viewer2.WaitForConnectionAsync();

        await viewer1.HubClient.ConnectTo(username, password);
        await viewer2.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;
        var viewer1Conn = await viewer1ConnTask;
        var viewer2Conn = await viewer2ConnTask;

        var viewer1ReceivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
        var viewer2ReceivedTcs = new TaskCompletionSource<ChatMessageDisplay>();

        viewer1Conn.Chat.MessageReceived += (s, msg) => viewer1ReceivedTcs.TrySetResult(msg);
        viewer2Conn.Chat.MessageReceived += (s, msg) => viewer2ReceivedTcs.TrySetResult(msg);

        // Presenter sends message
        await presenterConn.Chat.SendMessageAsync("Broadcast to all!");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() =>
        {
            viewer1ReceivedTcs.TrySetCanceled();
            viewer2ReceivedTcs.TrySetCanceled();
        });

        var msg1 = await viewer1ReceivedTcs.Task;
        var msg2 = await viewer2ReceivedTcs.Task;

        await Assert.That(msg1.Text).IsEqualTo("Broadcast to all!");
        await Assert.That(msg2.Text).IsEqualTo("Broadcast to all!");
    }

    [Test]
    public async Task ChatMessagesContainCorrectSenderInfo()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            // Test 1: Viewer sends to presenter - verify IsFromPresenter is false
            var presenterReceivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
            presenterConn.Chat.MessageReceived += (s, msg) =>
            {
                if (msg.Text == "From viewer")
                    presenterReceivedTcs.TrySetResult(msg);
            };

            await viewerConn.Chat.SendMessageAsync("From viewer");
            using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts1.Token.Register(() => presenterReceivedTcs.TrySetCanceled());
            var fromViewer = await presenterReceivedTcs.Task;

            await Assert.That(fromViewer.IsFromPresenter).IsFalse();

            // Test 2: Presenter sends to viewer - verify IsFromPresenter is true
            var viewerReceivedTcs = new TaskCompletionSource<ChatMessageDisplay>();
            viewerConn.Chat.MessageReceived += (s, msg) =>
            {
                if (msg.Text == "From presenter")
                    viewerReceivedTcs.TrySetResult(msg);
            };

            await presenterConn.Chat.SendMessageAsync("From presenter");
            using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts2.Token.Register(() => viewerReceivedTcs.TrySetCanceled());
            var fromPresenter = await viewerReceivedTcs.Task;

            await Assert.That(fromPresenter.IsFromPresenter).IsTrue();
        }
    }

    #endregion

    #region Multi-Viewer Scenario Tests

    [Test]
    public async Task ViewerCanBeBlockedWhileOthersAreNot()
    {
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer1 = new ClientFixture(serverFixture, "Viewer1");
        await using var viewer2 = new ClientFixture(serverFixture, "Viewer2");

        await presenter.HubClient.ConnectToHub();
        await viewer1.HubClient.ConnectToHub();
        await viewer2.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewer1ConnTask = viewer1.WaitForConnectionAsync();
        var viewer2ConnTask = viewer2.WaitForConnectionAsync();

        await viewer1.HubClient.ConnectTo(username, password);
        await viewer2.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;
        var viewer1Conn = await viewer1ConnTask;
        var viewer2Conn = await viewer2ConnTask;

        // Block only viewer1
        var viewer1ClientId = viewer1.HubClient.ClientId!;
        await presenterConn.UpdateConnectionPropertiesAndSend(props =>
            props with { InputBlockedViewerIds = [viewer1ClientId] });

        await Task.Delay(200);

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
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer1 = new ClientFixture(serverFixture, "Viewer1");
        await using var viewer2 = new ClientFixture(serverFixture, "Viewer2");

        await presenter.HubClient.ConnectToHub();
        await viewer1.HubClient.ConnectToHub();
        await viewer2.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewer1ConnTask = viewer1.WaitForConnectionAsync();
        var viewer2ConnTask = viewer2.WaitForConnectionAsync();

        await viewer1.HubClient.ConnectTo(username, password);
        await viewer2.HubClient.ConnectTo(username, password);

        var presenterConn = await presenterConnTask;
        var viewer1Conn = await viewer1ConnTask;
        var viewer2Conn = await viewer2ConnTask;

        var messagesReceived = 0;
        var allReceivedTcs = new TaskCompletionSource();

        viewer1Conn.Chat.MessageReceived += (s, msg) =>
        {
            if (Interlocked.Increment(ref messagesReceived) >= 2)
                allReceivedTcs.TrySetResult();
        };
        viewer2Conn.Chat.MessageReceived += (s, msg) =>
        {
            if (Interlocked.Increment(ref messagesReceived) >= 2)
                allReceivedTcs.TrySetResult();
        };

        await presenterConn.Chat.SendMessageAsync("Broadcast message");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        cts.Token.Register(() => allReceivedTcs.TrySetCanceled());
        await allReceivedTcs.Task;

        await Assert.That(messagesReceived).IsEqualTo(2);
    }

    #endregion

    #region Display Selection Tests

    [Test]
    public async Task AvailableDisplaysChangedEventFires()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var displaysChangedTcs = new TaskCompletionSource();
            viewerConn.RequiredViewerService.AvailableDisplaysChanged += (s, e) =>
            {
                if (viewerConn.RequiredViewerService.AvailableDisplays.Count >= 2)
                    displaysChangedTcs.TrySetResult();
            };

            // Update connection properties with new displays (simulating display change)
            var newDisplays = new List<DisplayInfo>
            {
                new("DISPLAY1", "Display 1", true, 0, 0, 1920, 1080),
                new("DISPLAY2", "Display 2", false, 1920, 0, 3840, 1080)
            };

            await presenterConn.UpdateConnectionPropertiesAndSend(props =>
                props with { AvailableDisplays = newDisplays }, forceSend: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => displaysChangedTcs.TrySetCanceled());
            await displaysChangedTcs.Task;

            await Assert.That(viewerConn.RequiredViewerService.AvailableDisplays.Count).IsGreaterThanOrEqualTo(2);
        }
    }

    #endregion

    #region File Transfer Tests

    [Test]
    public async Task FileTransferRequestIsSentToPresenter()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
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
    }

    [Test]
    public async Task RejectedFileTransferDoesNotComplete()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            // Configure presenter's dialog to reject file transfer
            presenter.DialogService.ShowFileTransferConfirmationAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>())
                .Returns(Task.FromResult(false));

            // Track transfer failure
            var failedTcs = new TaskCompletionSource<bool>();
            viewerConn.FileTransfers.TransferFailed += (s, e) => failedTcs.TrySetResult(true);

            // Create a temp file to transfer
            var tempFile = Path.GetTempFileName();
            try
            {
                await File.WriteAllTextAsync(tempFile, "Test file content");

                // Start the transfer
                var sendTask = viewerConn.FileTransfers.SendFileAsync(tempFile);

                // Wait for failure or timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                cts.Token.Register(() => failedTcs.TrySetCanceled());

                var failed = await failedTcs.Task;
                await Assert.That(failed).IsTrue();
            }
            finally
            {
                File.Delete(tempFile);
            }
        }
    }

    #endregion

    #region Clipboard Sync Tests

    [Test]
    public async Task TextClipboardSyncsFromViewerToPresenter()
    {
        // Create fixtures with clipboard already configured BEFORE connection
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer = new ClientFixture(serverFixture, "Viewer");

        // Configure viewer's clipboard to return text BEFORE connecting
        viewer.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from viewer"));

        await presenter.HubClient.ConnectToHub();
        await viewer.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewerConnTask = viewer.WaitForConnectionAsync();

        await viewer.HubClient.ConnectTo(username, password);

        await presenterConnTask;
        await viewerConnTask;

        // Advance time to trigger clipboard poll (500ms + some margin)
        viewer.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));

        // Allow async work to process (needs time for SignalR propagation)
        await Task.Delay(1000);

        // Verify presenter's clipboard was set with the text
        await presenter.ClipboardService.Received().SetTextAsync("Clipboard text from viewer");
    }

    [Test]
    public async Task TextClipboardSyncsFromPresenterToViewer()
    {
        // Create fixtures with clipboard already configured BEFORE connection
        await using var presenter = new ClientFixture(serverFixture, "Presenter");
        await using var viewer = new ClientFixture(serverFixture, "Viewer");

        // Configure presenter's clipboard to return text BEFORE connecting
        presenter.ClipboardService.TryGetTextAsync()
            .Returns(Task.FromResult<string?>("Clipboard text from presenter"));

        await presenter.HubClient.ConnectToHub();
        await viewer.HubClient.ConnectToHub();

        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewerConnTask = viewer.WaitForConnectionAsync();

        await viewer.HubClient.ConnectTo(username, password);

        await presenterConnTask;
        await viewerConnTask;

        // Advance time to trigger clipboard poll (500ms + some margin)
        presenter.TimeProvider.Advance(TimeSpan.FromMilliseconds(600));

        // Allow async work to process (needs time for SignalR propagation)
        await Task.Delay(1000);

        // Verify viewer's clipboard was set with the text
        await viewer.ClipboardService.Received().SetTextAsync("Clipboard text from presenter");
    }

    #endregion
}

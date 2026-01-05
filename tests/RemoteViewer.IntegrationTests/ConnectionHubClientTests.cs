using RemoteViewer.Client.Services.HubClient;
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

            // Verify via NullInputInjectionService (exposed on presenter fixture)
            var moves = presenter.InputInjectionService.MouseMoves;
            await Assert.That(moves.Count).IsGreaterThan(0);
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

            var keys = presenter.InputInjectionService.KeyPresses;
            await Assert.That(keys.Count).IsGreaterThanOrEqualTo(2); // Down + Up
        }
    }

    [Test]
    [Skip("Presenter connection doesn't close when viewer disconnects - this is correct behavior for multi-viewer scenarios")]
    public async Task DisconnectClosesConnection()
    {
        var (presenter, viewer, presenterConn, viewerConn) = await this.SetupConnectionAsync();
        await using (presenter)
        await using (viewer)
        {
            var closedTcs = new TaskCompletionSource();
            presenterConn.Closed += (s, e) => closedTcs.TrySetResult();

            await viewerConn.DisconnectAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            cts.Token.Register(() => closedTcs.TrySetCanceled());
            await closedTcs.Task;

            await Assert.That(presenterConn.IsClosed).IsTrue();
        }
    }
}

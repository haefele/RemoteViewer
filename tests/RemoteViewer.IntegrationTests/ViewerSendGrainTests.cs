using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.TestFixtures.Fixtures;
using RemoteViewer.Shared.Protocol;
using System.Reflection;

namespace RemoteViewer.IntegrationTests;

[NotInParallel]
public class ViewerSendGrainTests()
{
    private static readonly ulong[] s_expectedFrames_1_3 = [1UL, 3UL];

    [ClassDataSource<ServerFixture>(Shared = SharedType.PerTestSession)]
    public required ServerFixture Server { get; init; }

    [Test]
    public async Task FramesCoalesceLatestWinsPerViewer()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);

        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;
        await InvokePresenterSelectDisplayAsync(presenterConn, viewer.HubClient.ClientId!, "DISPLAY1");

        // Suppress auto-acks so we can control timing manually
        viewer.HubClient.Options.SuppressAutoFrameAck = true;

        var receivedFrames = new List<ulong>();
        var firstFrameReceived = new TaskCompletionSource();

        viewerConn.RequiredViewerService.FrameReady += (_, args) =>
        {
            receivedFrames.Add(args.FrameNumber);
            firstFrameReceived.TrySetResult();
        };

        // Send 20 frames rapidly - frame 1 goes immediately, rest coalesce
        for (var i = 1; i <= 20; i++)
        {
            await InvokeSendFrameAsync(presenterConn, (ulong)i);
        }

        // Wait for frame 1 to arrive
        await firstFrameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(receivedFrames).Contains(1UL);

        // Give time for frames 2-20 to coalesce on server
        await Task.Delay(100);

        // Ack frame 1 - should get frame 20 (latest)
        var secondFrameReceived = new TaskCompletionSource();
        viewerConn.RequiredViewerService.FrameReady += (_, args) =>
        {
            if (args.FrameNumber != 1)
                secondFrameReceived.TrySetResult();
        };

        await SendAckFrameAsync(viewer.HubClient);
        await secondFrameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(receivedFrames[^1]).IsEqualTo(20UL);
    }

    [Test]
    public async Task ViewerSendQueueDropOldestWhenBusy()
    {
        await using var presenter = await this.Server.CreateClientAsync("Presenter");
        await using var viewer = await this.Server.CreateClientAsync("Viewer");
        await this.Server.CreateConnectionAsync(presenter, viewer);

        var presenterConn = presenter.CurrentConnection!;
        var viewerConn = viewer.CurrentConnection!;
        var viewerService = viewerConn.RequiredViewerService;
        await InvokePresenterSelectDisplayAsync(presenterConn, viewer.HubClient.ClientId!, "DISPLAY1");

        // Suppress auto-acks so we can control timing manually
        viewer.HubClient.Options.SuppressAutoFrameAck = true;

        var receivedFrames = new List<ulong>();
        var firstFrameReceived = new TaskCompletionSource();
        var secondFrameReceived = new TaskCompletionSource();

        viewerService.FrameReady += (_, args) =>
        {
            receivedFrames.Add(args.FrameNumber);
            if (receivedFrames.Count == 1)
                firstFrameReceived.TrySetResult();
            else if (receivedFrames.Count >= 2)
                secondFrameReceived.TrySetResult();
        };

        // Send frame 1 - should be delivered immediately
        await InvokeSendFrameAsync(presenterConn, 1);

        // Wait for frame 1 to actually arrive before sending more
        await firstFrameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // DON'T ack frame 1 yet - send frames 2 and 3
        await InvokeSendFrameAsync(presenterConn, 2);
        await InvokeSendFrameAsync(presenterConn, 3);
        await Task.Delay(100); // Let frames 2 and 3 coalesce on server

        // Now ack - server should send the latest buffered frame (3, not 2)
        await SendAckFrameAsync(viewer.HubClient);

        await secondFrameReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(receivedFrames).IsEquivalentTo(s_expectedFrames_1_3);
    }

    private static Task SendAckFrameAsync(ConnectionHubClient client)
    {
        var method = client.GetType().GetMethod("SendAckFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(client, Array.Empty<object>())!;
    }

    private static Task InvokeSendFrameAsync(Connection connection, ulong frameNumber)
    {
        var method = connection.GetType().GetMethod("RemoteViewer.Client.Services.HubClient.IConnectionImpl.SendFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(connection, ["DISPLAY1", frameNumber, FrameCodec.Jpeg90, Array.Empty<FrameRegion>()])!;
    }

    private static Task InvokePresenterSelectDisplayAsync(Connection connection, string viewerClientId, string displayId)
    {
        var service = connection.PresenterService ?? throw new InvalidOperationException("Presenter service not available.");
        var method = service.GetType().GetMethod("RemoteViewer.Client.Services.HubClient.IPresenterServiceImpl.SelectViewerDisplayAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(service, [viewerClientId, displayId, CancellationToken.None])!;
    }
}

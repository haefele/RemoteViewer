using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.TestFixtures.Fixtures;
using RemoteViewer.Shared.Protocol;
using System.Reflection;
using static RemoteViewer.TestFixtures.TestHelpers;

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

        viewerConn.RequiredViewerService.FrameReady += (_, args) =>
        {
            receivedFrames.Add(args.FrameNumber);
        };

        // Send 20 frames rapidly - frame 1 goes immediately, rest coalesce
        for (var i = 1; i <= 20; i++)
        {
            await InvokeSendFrameAsync(presenterConn, (ulong)i);
        }

        // Wait for frame 1 to arrive
        await WaitUntil(
            () => receivedFrames.Contains(1UL),
            message: "Frame 1 was not received");

        // Ack and wait for frame 20 (frames should coalesce to latest)
        // Retry acking if frame 20 hasn't arrived yet (frames might still be in transit)
        await WaitUntil(
            async () =>
            {
                await SendAckFrameAsync(viewer.HubClient, viewerConn.ConnectionId);
                await Task.Delay(100); // Give time for frame to arrive
                return receivedFrames.Contains(20UL);
            },
            message: "Frame 20 was not received after acking");

        // Verify the last received frame is 20
        await Assert.That(receivedFrames.Count).IsEqualTo(2);
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

        viewerService.FrameReady += (_, args) =>
        {
            receivedFrames.Add(args.FrameNumber);
        };

        // Send frame 1 - should be delivered immediately
        await InvokeSendFrameAsync(presenterConn, 1);

        // Wait for frame 1 to arrive
        await WaitUntil(
            () => receivedFrames.Contains(1UL),
            message: "Frame 1 was not received");

        // DON'T ack frame 1 yet - send frames 2 and 3
        await InvokeSendFrameAsync(presenterConn, 2);
        await InvokeSendFrameAsync(presenterConn, 3);

        // Ack and wait for frame 3 (frames should coalesce, dropping frame 2)
        // Retry acking if frame 3 hasn't arrived yet (frames might still be in transit)
        await WaitUntil(
            async () =>
            {
                await SendAckFrameAsync(viewer.HubClient, viewerConn.ConnectionId);
                await Task.Delay(100); // Give time for frame to arrive
                return receivedFrames.Contains(3UL);
            },
            message: "Frame 3 was not received after acking");

        // Verify we got exactly frames 1 and 3 (frame 2 was dropped)
        await Assert.That(receivedFrames).IsEquivalentTo(s_expectedFrames_1_3);
    }

    private static Task SendAckFrameAsync(ConnectionHubClient client, string connectionId)
    {
        var method = client.GetType().GetMethod("SendAckFrameAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(client, [connectionId])!;
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

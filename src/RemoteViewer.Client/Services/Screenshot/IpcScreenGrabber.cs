using RemoteViewer.Client.Services.WindowsIpc;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.Screenshot;

public class IpcScreenGrabber(SessionRecorderRpcClient rpcClient) : IScreenGrabber
{
    public bool IsAvailable => true;
    public int Priority => 200;

    public async Task<GrabResult> CaptureDisplay(DisplayInfo display, bool forceKeyframe, CancellationToken ct)
    {
        if (rpcClient.IsConnected is false)
            return new GrabResult { Status = GrabStatus.Failure };

        var dto = await rpcClient.Proxy!.CaptureDisplay(display.Id, forceKeyframe, ct);
        return dto.FromDto();
    }
}

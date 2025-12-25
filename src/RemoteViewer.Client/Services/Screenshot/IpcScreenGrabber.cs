using RemoteViewer.Client.Services.WindowsIpc;

namespace RemoteViewer.Client.Services.Screenshot;

public class IpcScreenGrabber(SessionRecorderRpcClient rpcClient) : IScreenGrabber
{
    public bool IsAvailable => true;
    public int Priority => 200;

    public async Task<GrabResult> CaptureDisplay(Display display, bool forceKeyframe, CancellationToken ct)
    {
        if (rpcClient.IsConnected is false)
            return new GrabResult { Status = GrabStatus.Failure };

        var dto = await rpcClient.Proxy!.CaptureDisplay(display.Name, forceKeyframe, ct);
        return dto.FromDto();
    }
}

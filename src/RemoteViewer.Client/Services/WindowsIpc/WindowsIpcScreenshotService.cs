using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsIpcScreenshotService(
    SessionRecorderRpcClient rpcClient,
    ILogger<WindowsIpcScreenshotService> logger) : IScreenshotService
{
    public async Task<GrabResult> CaptureDisplay(Display display, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");

        var dto = await proxy.CaptureDisplay(display.Name, ct);
        return dto.FromDto();
    }

    public async Task ForceKeyframe(string displayName, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");

        await proxy.ForceKeyframe(displayName, ct);
        logger.LogDebug("Forced keyframe for display {DisplayName} via SessionRecorder service", displayName);
    }
}

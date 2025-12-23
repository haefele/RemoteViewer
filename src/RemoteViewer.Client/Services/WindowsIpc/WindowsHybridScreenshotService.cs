using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsHybridScreenshotService(
    ScreenshotService localService,
    WindowsIpcScreenshotService ipcService,
    SessionRecorderRpcClient rpcClient,
    ILogger<WindowsHybridScreenshotService> logger) : IScreenshotService
{
    public async Task<GrabResult> CaptureDisplay(Display display, CancellationToken ct)
    {
        if (rpcClient.IsConnected)
        {
            try
            {
                return await ipcService.CaptureDisplay(display, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to capture display via IPC, falling back to local service");
            }
        }

        return await localService.CaptureDisplay(display, ct);
    }

    public async Task ForceKeyframe(string displayName, CancellationToken ct)
    {
        if (rpcClient.IsConnected)
        {
            try
            {
                await ipcService.ForceKeyframe(displayName, ct);
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to force keyframe via IPC, falling back to local service");
            }
        }

        await localService.ForceKeyframe(displayName, ct);
    }
}

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsHybridDisplayService(
    WindowsDisplayService localService,
    WindowsIpcDisplayService ipcService,
    SessionRecorderRpcClient rpcClient,
    ILogger<WindowsHybridDisplayService> logger) : IDisplayService
{
    public async Task<ImmutableList<Display>> GetDisplays(CancellationToken ct)
    {
        if (rpcClient.IsConnected)
        {
            try
            {
                return await ipcService.GetDisplays(ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get displays via IPC, falling back to local service");
            }
        }

        return await localService.GetDisplays(ct);
    }
}

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.Screenshot;
using ZiggyCreatures.Caching.Fusion;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsHybridDisplayService(
    WindowsDisplayService localService,
    SessionRecorderRpcClient rpcClient,
    IFusionCache cache,
    ILogger<WindowsHybridDisplayService> logger) : IDisplayService
{
    private const string CacheKey = "displays-ipc";

    private static readonly FusionCacheEntryOptions s_cacheOptions = new(TimeSpan.FromSeconds(10))
    {
        EagerRefreshThreshold = 0.8f,
    };

    public async Task<ImmutableList<Display>> GetDisplays(CancellationToken ct)
    {
        if (rpcClient.IsConnected)
        {
            try
            {
                return await cache.GetOrSetAsync<ImmutableList<Display>>(
                    CacheKey,
                    async (_, ct2) =>
                    {
                        var proxy = rpcClient.Proxy!;
                        var dtos = await proxy.GetDisplays(ct2);
                        logger.LogDebug("Retrieved {Count} displays from SessionRecorder service", dtos.Length);
                        return dtos.Select(d => d.FromDto()).ToImmutableList();
                    },
                    s_cacheOptions,
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get displays via IPC, falling back to local service");
            }
        }

        return await localService.GetDisplays(ct);
    }
}

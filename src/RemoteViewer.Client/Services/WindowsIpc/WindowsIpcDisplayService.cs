using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.Screenshot;
using ZiggyCreatures.Caching.Fusion;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsIpcDisplayService(
    SessionRecorderRpcClient rpcClient,
    IFusionCache cache,
    ILogger<WindowsIpcDisplayService> logger) : IDisplayService
{
    private const string CacheKey = "displays";

    private static readonly FusionCacheEntryOptions s_cacheOptions = new(TimeSpan.FromSeconds(10))
    {
        EagerRefreshThreshold = 0.8f, // Refresh in background when 80% of duration has passed
    };

    public async Task<ImmutableList<Display>> GetDisplays(CancellationToken ct)
    {
        return await cache.GetOrSetAsync<ImmutableList<Display>>(
            CacheKey,
            async (_, ct2) =>
            {
                var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
                var dtos = await proxy.GetDisplays(ct2);
                logger.LogDebug("Retrieved {Count} displays from SessionRecorder service", dtos.Length);
                return dtos.Select(d => d.FromDto()).ToImmutableList();
            },
            s_cacheOptions,
            ct);
    }
}

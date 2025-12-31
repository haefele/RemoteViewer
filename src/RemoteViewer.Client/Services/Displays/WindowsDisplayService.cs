#if WINDOWS
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Foundation;
using RemoteViewer.Client.Services.WindowsIpc;
using RemoteViewer.Server.SharedAPI;
using ZiggyCreatures.Caching.Fusion;

namespace RemoteViewer.Client.Services.Displays;

public class WindowsDisplayService(
    SessionRecorderRpcClient? rpcClient,
    IFusionCache cache,
    ILogger<WindowsDisplayService> logger) : IDisplayService
{
    private const string CacheKeyIpc = "displays-ipc";
    private const string CacheKeyLocal = "displays-local";

    private static readonly FusionCacheEntryOptions s_cacheOptions = new(TimeSpan.FromSeconds(10))
    {
        EagerRefreshThreshold = 0.8f,
    };

    public async Task<ImmutableList<DisplayInfo>> GetDisplays(string? connectionId, CancellationToken ct)
    {
        if (connectionId is not null && rpcClient is not null && rpcClient.IsConnected && rpcClient.IsAuthenticatedFor(connectionId))
        {
            try
            {
                return await cache.GetOrSetAsync<ImmutableList<DisplayInfo>>(
                    CacheKeyIpc,
                    async (_, ct2) =>
                    {
                        var proxy = rpcClient.Proxy!;
                        var dtos = await proxy.GetDisplays(connectionId, ct2);
                        logger.LogDebug("Retrieved {Count} displays from SessionRecorder service", dtos.Length);
                        return dtos.Select(d => d.ToDisplayInfo())
                            .OrderByDescending(d => d.IsPrimary)
                            .ThenBy(d => d.FriendlyName)
                            .ToImmutableList();
                    },
                    s_cacheOptions,
                    ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to get displays via IPC, falling back to local service");
            }
        }

        return await this.ActualGetDisplays(ct);
    }

    private Task<ImmutableList<DisplayInfo>> ActualGetDisplays(CancellationToken ct)
    {
        return cache.GetOrSetAsync<ImmutableList<DisplayInfo>>(
            CacheKeyLocal,
            (_, ct2) => this.EnumerateDisplaysAsync(ct2),
            s_cacheOptions,
            ct).AsTask();
    }

    private unsafe Task<ImmutableList<DisplayInfo>> EnumerateDisplaysAsync(CancellationToken ct)
    {
        try
        {
            var displays = new HashSet<DisplayInfo>(DisplayIdComparer.Instance);

            var result = (bool)PInvoke.EnumDisplayMonitors(HDC.Null, null, MonitorEnumCallback, new LPARAM(0));
            if (result is false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to enumerate display monitors: {ErrorCode}", errorCode);
                return Task.FromResult<ImmutableList<DisplayInfo>>([]);
            }

            if (displays.Count == 0)
            {
                logger.LogWarning("No displays found during enumeration");
            }

            return Task.FromResult(displays
                .OrderByDescending(d => d.IsPrimary)
                .ThenBy(d => d.FriendlyName)
                .ToImmutableList());

            BOOL MonitorEnumCallback(HMONITOR hMonitor, HDC hdc, RECT* lprcMonitor, LPARAM dwData)
            {
                var display = this.GetDisplayInfo(hMonitor, displays.Count);
                if (display is not null)
                    displays.Add(display);

                return true;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while getting displays");
            return Task.FromResult<ImmutableList<DisplayInfo>>([]);
        }
    }

    private unsafe DisplayInfo? GetDisplayInfo(HMONITOR hMonitor, int displayIndex)
    {
        try
        {
            const uint MONITORINFOF_PRIMARY = 0x00000001;

            var infoEx = new MONITORINFOEXW();
            infoEx.monitorInfo.cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>();

            if (PInvoke.GetMonitorInfo(hMonitor, ref infoEx.monitorInfo) == false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogWarning("Failed to get monitor info for handle {Handle}: {ErrorCode}", (nint)hMonitor.Value, errorCode);
                return null;
            }

            var id = ExtractDeviceName(infoEx.szDevice.AsSpan(), displayIndex);
            var isPrimary = (infoEx.monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;
            var rect = infoEx.monitorInfo.rcMonitor;
            var friendlyName = GetFriendlyDisplayName(id);

            return new DisplayInfo(id, friendlyName, isPrimary, rect.left, rect.top, rect.right, rect.bottom);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Exception occurred while getting display info for monitor handle {Handle}", (nint)hMonitor.Value);
            return null;
        }
    }

    private static string GetFriendlyDisplayName(string id)
    {
        var match = System.Text.RegularExpressions.Regex.Match(id, @"DISPLAY(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"Display {match.Groups[1].Value}";
        }
        return id;
    }

    private static string ExtractDeviceName(ReadOnlySpan<char> deviceBuffer, int fallbackIndex)
    {
        var nullIndex = deviceBuffer.IndexOf('\0');
        var name = nullIndex >= 0
            ? new string(deviceBuffer[..nullIndex])
            : new string(deviceBuffer);

        return !string.IsNullOrWhiteSpace(name)
            ? name
            : $"DISPLAY{fallbackIndex + 1}";
    }

    private sealed class DisplayIdComparer : IEqualityComparer<DisplayInfo>
    {
        public static readonly DisplayIdComparer Instance = new();

        private DisplayIdComparer() { }

        public bool Equals(DisplayInfo? x, DisplayInfo? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(DisplayInfo obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id);
        }
    }
}
#endif

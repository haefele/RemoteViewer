using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Foundation;
using SkiaSharp;
using System.Drawing;

namespace RemoteViewer.WinServ.Services;

public interface IScreenshotService
{
    ImmutableList<Display> GetDisplays();
    CaptureResult CaptureDisplay(Display display);
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public record CaptureResult(bool Success, SKBitmap? Bitmap, Rectangle[] DirtyRectangles)
{
    public static CaptureResult Failure => new(false, null, Array.Empty<Rectangle>());
    public static CaptureResult Ok(SKBitmap bitmap, Rectangle[] dirtyRectangles) => new(true, bitmap, dirtyRectangles);
    public static CaptureResult NoChanges => new(true, null, Array.Empty<Rectangle>());
}

public class ScreenshotService(ILogger<ScreenshotService> logger, DxgiScreenGrabber dxgi, BitBltScreenGrabber bitBlt) : IScreenshotService
{
    public unsafe ImmutableList<Display> GetDisplays()
    {
        try
        {
            var displays = new HashSet<Display>(DisplayNameComparer.Instance);

            var result = (bool)PInvoke.EnumDisplayMonitors(HDC.Null, null, MonitorEnumCallback, new LPARAM(0));
            if (result is false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to enumerate display monitors: {ErrorCode}", errorCode);
                return ImmutableList<Display>.Empty;
            }

            if (displays.Count == 0)
            {
                logger.LogWarning("No displays found during enumeration");
            }

            return displays.ToImmutableList();

            BOOL MonitorEnumCallback(HMONITOR hMonitor, HDC hdc, RECT* lprcMonitor, LPARAM dwData)
            {
                var display = GetDisplayInfo(hMonitor, displays.Count);
                if (display is not null)
                    displays.Add(display);

                return true;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while getting displays");
            return ImmutableList<Display>.Empty;
        }
    }

    public CaptureResult CaptureDisplay(Display display)
    {
        var dxgiResult = dxgi.CaptureDisplay(display);

        if (dxgiResult.Success)
            return dxgiResult;

        return bitBlt.CaptureDisplay(display);
    }

    private unsafe Display? GetDisplayInfo(HMONITOR hMonitor, int displayIndex)
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

            var name = ExtractDeviceName(infoEx.szDevice.AsSpan(), displayIndex);
            var isPrimary = (infoEx.monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;
            var bounds = CreateDisplayRect(infoEx.monitorInfo.rcMonitor);

            return new Display(name, isPrimary, bounds);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Exception occurred while getting display info for monitor handle {Handle}", (nint)hMonitor.Value);
            return null;
        }
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

    private static DisplayRect CreateDisplayRect(RECT rect)
    {
        return new DisplayRect(rect.left, rect.top, rect.right, rect.bottom);
    }

    private sealed class DisplayNameComparer : IEqualityComparer<Display>
    {
        public static readonly DisplayNameComparer Instance = new();

        private DisplayNameComparer() { }

        public bool Equals(Display? x, Display? y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(Display obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name);
        }
    }
    
}
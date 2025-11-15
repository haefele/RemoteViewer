using System.Collections.Immutable;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Foundation;

namespace RemoteViewer.WinServ.Services;

public interface IScreenshotService
{
    ImmutableList<Display> GetDisplays();
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public class ScreenshotService : IScreenshotService
{
    public unsafe ImmutableList<Display> GetDisplays()
    {
        var displays = new HashSet<Display>(DisplayNameComparer.Instance);

        PInvoke.EnumDisplayMonitors(HDC.Null, null, MonitorEnumCallback, new LPARAM(0));

        return displays.ToImmutableList();

        BOOL MonitorEnumCallback(HMONITOR hMonitor, HDC hdc, RECT* lprcMonitor, LPARAM dwData)
        {
            var display = GetDisplayInfo(hMonitor, displays.Count);
            if (display is not null)
                displays.Add(display);

            return true;
        }
    }

    private static Display? GetDisplayInfo(HMONITOR hMonitor, int displayIndex)
    {
        const uint MONITORINFOF_PRIMARY = 0x00000001;

        var infoEx = new MONITORINFOEXW();
        infoEx.monitorInfo.cbSize = (uint)Marshal.SizeOf<MONITORINFOEXW>();

        if (!PInvoke.GetMonitorInfo(hMonitor, ref infoEx.monitorInfo))
        {
            return null;
        }

        var name = ExtractDeviceName(infoEx.szDevice.AsSpan(), displayIndex);
        var isPrimary = (infoEx.monitorInfo.dwFlags & MONITORINFOF_PRIMARY) != 0;
        var bounds = CreateDisplayRect(infoEx.monitorInfo.rcMonitor);

        return new Display(name, isPrimary, bounds);
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
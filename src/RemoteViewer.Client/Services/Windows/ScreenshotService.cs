#if WINDOWS
using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.Foundation;

namespace RemoteViewer.Client.Services.Windows;

public class ScreenshotService(ILogger<ScreenshotService> logger, DxgiScreenGrabber dxgi, BitBltScreenGrabber bitBlt) : IScreenshotService
{
    private const long KeyframeIntervalMs = 3000;

    private readonly Dictionary<string, DisplayCaptureState> _displayStates = new();

    public bool IsSupported => true;

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
                return [];
            }

            if (displays.Count == 0)
            {
                logger.LogWarning("No displays found during enumeration");
            }

            return displays.ToImmutableList();

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
            return [];
        }
    }

    public unsafe CaptureResult CaptureDisplay(Display display)
    {
        var dxgiResult = dxgi.CaptureDisplay(display);

        if (!dxgiResult.Success)
        {
            // DXGI failed, try BitBlt fallback
            var bitBltResult = bitBlt.CaptureDisplay(display);
            return this.ProcessCaptureResult(display.Name, bitBltResult);
        }

        return this.ProcessCaptureResult(display.Name, dxgiResult);
    }

    private unsafe CaptureResult ProcessCaptureResult(string displayName, CaptureResult captureResult)
    {
        // If capture failed or no bitmap, pass through
        if (!captureResult.Success || captureResult.Bitmap is null)
        {
            return captureResult;
        }

        var state = this.GetOrCreateState(displayName);
        var bitmap = captureResult.Bitmap;

        // Check if keyframe is due (every 3 seconds)
        var forceKeyframe = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs;

        // Check if we have a valid previous frame to compare against
        var hasPreviousFrame = state.PreviousBitmap is not null &&
                                state.PreviousBitmap.Width == bitmap.Width &&
                                state.PreviousBitmap.Height == bitmap.Height;

        // If DXGI gave us dirty rects, use them (unless keyframe is due)
        if (captureResult.DirtyRectangles.Length > 0)
        {
            this.UpdateState(state, bitmap, forceKeyframe);
            if (forceKeyframe)
            {
                // Return empty dirty rects to signal keyframe
                return CaptureResult.Ok(bitmap, []);
            }
            return captureResult;
        }

        // No dirty rects from DXGI - compute manually if we have a previous frame
        if (hasPreviousFrame && !forceKeyframe)
        {
            var dirtyRects = this.ComputeDirtyRects(bitmap, state.PreviousBitmap!);

            if (dirtyRects is null)
            {
                // Threshold exceeded - send keyframe
                this.UpdateState(state, bitmap, resetKeyframeTimer: true);
                return CaptureResult.Ok(bitmap, []);
            }

            if (dirtyRects.Length == 0)
            {
                // No changes detected - skip this frame
                bitmap.Dispose();
                return CaptureResult.NoChanges;
            }

            // Return delta frame with computed dirty rects
            this.UpdateState(state, bitmap, resetKeyframeTimer: false);
            return CaptureResult.Ok(bitmap, dirtyRects);
        }

        // First frame, size changed, or keyframe interval - send keyframe
        this.UpdateState(state, bitmap, resetKeyframeTimer: true);
        return CaptureResult.Ok(bitmap, []);
    }

    private unsafe Rectangle[]? ComputeDirtyRects(SKBitmap current, SKBitmap previous)
    {
        var width = current.Width;
        var height = current.Height;
        var stride = current.RowBytes; // BGRA = 4 bytes per pixel

        var currentPixels = new ReadOnlySpan<byte>((void*)current.GetPixels(), height * stride);
        var previousPixels = new ReadOnlySpan<byte>((void*)previous.GetPixels(), height * stride);

        return FrameDiffDetector.DetectChanges(currentPixels, previousPixels, width, height, stride);
    }

    private DisplayCaptureState GetOrCreateState(string displayName)
    {
        if (!this._displayStates.TryGetValue(displayName, out var state))
        {
            state = new DisplayCaptureState();
            this._displayStates[displayName] = state;
        }
        return state;
    }

    private void UpdateState(DisplayCaptureState state, SKBitmap newBitmap, bool resetKeyframeTimer)
    {
        // Dispose old bitmap
        state.PreviousBitmap?.Dispose();

        // Clone the new bitmap for comparison (we don't own the original)
        state.PreviousBitmap = newBitmap.Copy();

        if (resetKeyframeTimer)
        {
            state.KeyframeTimer.Restart();
        }
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

    private sealed class DisplayCaptureState
    {
        public SKBitmap? PreviousBitmap { get; set; }
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
    }
}
#endif

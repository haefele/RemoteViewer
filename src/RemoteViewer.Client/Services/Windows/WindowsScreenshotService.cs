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

public class WindowsScreenshotService(ILogger<WindowsScreenshotService> logger, DxgiScreenGrabber dxgi, BitBltScreenGrabber bitBlt) : IScreenshotService, IDisposable
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

    public CaptureResult CaptureDisplay(Display display)
    {
        var state = this.GetOrCreateState(display.Name);
        var targetBuffer = state.GetOrCreateNextBuffer(display.Bounds.Width, display.Bounds.Height);

        var result = dxgi.CaptureDisplay(display, targetBuffer);

        // DXGI failed, try BitBlt fallback
        if (result.Success is false)
            result = bitBlt.CaptureDisplay(display, targetBuffer);

        return this.ProcessCaptureResult(result, state);
    }

    private CaptureResult ProcessCaptureResult(CaptureResult captureResult, DisplayCaptureState state)
    {
        if (!captureResult.Success || captureResult.Bitmap is null)
            return captureResult;

        var bitmap = captureResult.Bitmap;
        var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs || state.ForceNextKeyframe;

        // Determine dirty rects: use DXGI-provided, compute manually, or send keyframe
        var dirtyRects = captureResult.DirtyRectangles.Length > 0
            ? captureResult.DirtyRectangles
            : state.LastCapturedBitmap is not null && !keyframeDue
                ? this.ComputeDirtyRects(bitmap, state.LastCapturedBitmap)
                : null;

        // null means threshold exceeded or keyframe needed - treat as keyframe
        var isKeyframe = keyframeDue || dirtyRects is null;

        // No changes detected - don't swap buffers, frame wasn't "used"
        if (dirtyRects is { Length: 0 })
            return CaptureResult.NoChanges;

        state.SwapBuffers();

        if (isKeyframe)
        {
            state.KeyframeTimer.Restart();
            state.ForceNextKeyframe = false;
        }

        return CaptureResult.Ok(bitmap, isKeyframe ? [] : dirtyRects!);
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
            // First capture for a display should always be a keyframe
            state = new DisplayCaptureState { ForceNextKeyframe = true };
            this._displayStates[displayName] = state;
        }
        return state;
    }

    public void RequestKeyframe(string displayName)
    {
        // When a new viewer selects a display, force a keyframe on the next capture cycle.
        // This ensures new viewers don't see a black screen while waiting for the regular keyframe interval to expire.
        var state = this.GetOrCreateState(displayName);
        state.ForceNextKeyframe = true;
    }

    public void Dispose()
    {
        foreach (var state in this._displayStates.Values)
        {
            state.Dispose();
        }
        this._displayStates.Clear();
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

    private sealed class DisplayCaptureState : IDisposable
    {
        private readonly SKBitmap?[] _buffers = new SKBitmap?[2];
        private int _currentIndex;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
        public bool ForceNextKeyframe { get; set; }

        public SKBitmap? LastCapturedBitmap => this._buffers[this._currentIndex];

        public SKBitmap GetOrCreateNextBuffer(int width, int height)
        {
            var nextIndex = 1 - this._currentIndex;

            if (this._buffers[nextIndex] is null || this.Width != width || this.Height != height)
            {
                this._buffers[nextIndex]?.Dispose();
                this._buffers[nextIndex] = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                this.Width = width;
                this.Height = height;

                if (this._buffers[this._currentIndex] is not null &&
                    (this._buffers[this._currentIndex]!.Width != width || this._buffers[this._currentIndex]!.Height != height))
                {
                    this._buffers[this._currentIndex]?.Dispose();
                    this._buffers[this._currentIndex] = null;
                }
            }

            return this._buffers[nextIndex]!;
        }

        public void SwapBuffers() => this._currentIndex = 1 - this._currentIndex;

        public void Dispose()
        {
            this._buffers[0]?.Dispose();
            this._buffers[1]?.Dispose();
            this._buffers[0] = null;
            this._buffers[1] = null;
        }
    }
}
#endif

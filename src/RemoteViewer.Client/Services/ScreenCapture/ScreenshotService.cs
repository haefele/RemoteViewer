using System.Diagnostics;
using System.Drawing;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.VideoCodec;

namespace RemoteViewer.Client.Services.ScreenCapture;

public sealed class ScreenshotService : IScreenshotService, IDisposable
{
    private const int KeyframeIntervalMs = 1000;

    private readonly ILogger<ScreenshotService> _logger;
    private readonly IScreenGrabber[] _sortedGrabbers;
    private readonly Dictionary<string, DisplayState> _displayStates = new();
    private readonly object _lock = new();

    public ScreenshotService(
        ILogger<ScreenshotService> logger,
        IEnumerable<IScreenGrabber> grabbers)
    {
        this._logger = logger;

        // Sort grabbers by priority descending, filter to available ones
        this._sortedGrabbers = grabbers
            .Where(g => g.IsAvailable)
            .OrderByDescending(g => g.Priority)
            .ToArray();
    }

    public bool IsSupported => this._sortedGrabbers.Length > 0;

    public GrabResult CaptureDisplay(Display display)
    {
        var state = this.GetOrCreateDisplayState(display.Name);

        // Determine if keyframe is due
        var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs
                          || state.ForceNextKeyframe;

        // Try grabbers in priority order
        foreach (var grabber in this._sortedGrabbers)
        {
            var result = grabber.CaptureDisplay(display, forceKeyframe: keyframeDue);

            if (result.Status == GrabStatus.Success)
            {
                // Got a full frame (keyframe)
                if (result.FullFramePixels is not null)
                {
                    // Reset keyframe timer
                    state.KeyframeTimer.Restart();
                    state.ForceNextKeyframe = false;

                    // If keyframe was not due but we got a full frame (BitBlt),
                    // use software diff to extract dirty regions
                    if (!keyframeDue)
                    {
                        return this.ApplySoftwareDiff(result, state, display);
                    }

                    // Keyframe was requested, return full frame
                    return result;
                }

                // Got dirty regions from hardware (DXGI)
                if (result.DirtyRegions is { Length: > 0 })
                {
                    return result;
                }

                // Success but no data - shouldn't happen, force keyframe next time
                state.ForceNextKeyframe = true;
                return new GrabResult(GrabStatus.NoChanges, null, null, null);
            }

            if (result.Status == GrabStatus.NoChanges)
            {
                return result;
            }

            // GrabStatus.Failure - try next grabber
            this._logger.LogDebug("Grabber {GrabberType} failed for display {Display}, trying next",
                grabber.GetType().Name, display.Name);
        }

        // All grabbers failed
        return new GrabResult(GrabStatus.Failure, null, null, null);
    }

    private GrabResult ApplySoftwareDiff(GrabResult result, DisplayState state, Display display)
    {
        var fullFrame = result.FullFramePixels!;
        var width = display.Bounds.Width;
        var height = display.Bounds.Height;

        var softwareDirtyRects = state.DiffDetector.DetectChanges(
            fullFrame.Span,
            width,
            height,
            width * 4);

        // null = too many changes, send as keyframe
        if (softwareDirtyRects is null)
        {
            state.KeyframeTimer.Restart();
            state.ForceNextKeyframe = false;
            return result;
        }

        // Empty array = no changes
        if (softwareDirtyRects.Length == 0)
        {
            fullFrame.Dispose();
            return new GrabResult(GrabStatus.NoChanges, null, null, null);
        }

        // Extract dirty regions from full frame
        var dirtyRegions = this.ExtractDirtyRegions(fullFrame.Span, softwareDirtyRects, width);
        fullFrame.Dispose();

        return new GrabResult(GrabStatus.Success, null, dirtyRegions, null);
    }

    private DirtyRegion[] ExtractDirtyRegions(ReadOnlySpan<byte> fullFrame, Rectangle[] dirtyRects, int frameWidth)
    {
        var regions = new DirtyRegion[dirtyRects.Length];

        for (var i = 0; i < dirtyRects.Length; i++)
        {
            var rect = dirtyRects[i];
            var regionBufferSize = rect.Width * rect.Height * 4;
            var regionMemory = RefCountedMemoryOwner<byte>.Create(regionBufferSize);

            // Copy the rect's pixels from the full frame to compact buffer
            var regionSpan = regionMemory.Span;
            var srcStride = frameWidth * 4;
            var destStride = rect.Width * 4;

            for (var y = 0; y < rect.Height; y++)
            {
                var srcOffset = (rect.Y + y) * srcStride + rect.X * 4;
                var destOffset = y * destStride;
                fullFrame.Slice(srcOffset, destStride).CopyTo(regionSpan.Slice(destOffset, destStride));
            }

            regions[i] = new DirtyRegion(rect.X, rect.Y, rect.Width, rect.Height, regionMemory);
        }

        return regions;
    }

    private DisplayState GetOrCreateDisplayState(string displayName)
    {
        lock (this._lock)
        {
            if (!this._displayStates.TryGetValue(displayName, out var state))
            {
                state = new DisplayState();
                this._displayStates[displayName] = state;
            }
            return state;
        }
    }

    public void ForceKeyframe(string displayName)
    {
        lock (this._lock)
        {
            if (this._displayStates.TryGetValue(displayName, out var state))
            {
                state.ForceNextKeyframe = true;
            }
        }
    }

    public void Dispose()
    {
        lock (this._lock)
        {
            foreach (var state in this._displayStates.Values)
            {
                state.Dispose();
            }
            this._displayStates.Clear();
        }
    }

    private sealed class DisplayState : IDisposable
    {
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
        public bool ForceNextKeyframe { get; set; } = true;
        public FrameDiffDetector DiffDetector { get; } = new();

        public void Dispose()
        {
            this.DiffDetector.Dispose();
        }
    }
}

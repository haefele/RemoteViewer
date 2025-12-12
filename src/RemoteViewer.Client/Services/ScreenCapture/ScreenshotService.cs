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

        this._sortedGrabbers = grabbers
            .Where(g => g.IsAvailable)
            .OrderByDescending(g => g.Priority)
            .ToArray();
    }

    public bool IsSupported => this._sortedGrabbers.Length > 0;

    public GrabResult CaptureDisplay(Display display)
    {
        var state = this.GetOrCreateDisplayState(display.Name);
        var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs || state.ForceNextKeyframe;

        foreach (var grabber in this._sortedGrabbers)
        {
            var result = grabber.CaptureDisplay(display, keyframeDue);

            if (result.Status == GrabStatus.NoChanges)
            {
                return result;
            }
            else if (result.Status == GrabStatus.Failure)
            {
                this._logger.LogDebug("ScreenGrabber {GrabberType} failed for display {Display}, trying next", grabber.GetType().Name, display.Name);
                continue;
            }
            else // Success
            {
                // Got a full frame (keyframe)
                if (result.FullFramePixels is not null)
                {
                    // Reset keyframe timer
                    state.KeyframeTimer.Restart();
                    state.ForceNextKeyframe = false;

                    return result;
                }

                if (result.DirtyRegions is null)
                {
                    return this.ApplySoftwareDiff(result, state, display);
                }
                else
                {
                    return result;
                }
            }
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

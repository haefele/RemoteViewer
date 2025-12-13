using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.Screenshot;

public sealed class ScreenshotService : IScreenshotService
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
                    state.KeyframeTimer.Restart();
                    state.ForceNextKeyframe = false;
                }

                return result;
            }
        }

        // All grabbers failed
        return new GrabResult(GrabStatus.Failure, null, null, null);
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

    private sealed class DisplayState
    {
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
        public bool ForceNextKeyframe { get; set; } = true;
    }
}

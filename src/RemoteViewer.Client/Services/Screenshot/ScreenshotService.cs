using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.Screenshot;

public sealed class ScreenshotService : IScreenshotService
{
    private const int KeyframeIntervalMs = 1000;

    private readonly ILogger<ScreenshotService> _logger;
    private readonly IScreenGrabber[] _sortedGrabbers;
    private readonly ConcurrentDictionary<string, DisplayState> _displayStates = new();

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

    public Task<GrabResult> CaptureDisplay(Display display, CancellationToken ct)
    {
        var state = this.GetOrCreateDisplayState(display.Name);
        var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs || state.ForceNextKeyframe;

        foreach (var grabber in this._sortedGrabbers)
        {
            var result = grabber.CaptureDisplay(display, keyframeDue);

            if (result.Status == GrabStatus.NoChanges)
            {
                return Task.FromResult(result);
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

                return Task.FromResult(result);
            }
        }

        // All grabbers failed
        return Task.FromResult(new GrabResult(GrabStatus.Failure, null, null, null));
    }

    private DisplayState GetOrCreateDisplayState(string displayName)
    {
        return this._displayStates.GetOrAdd(displayName, _ => new DisplayState());
    }

    public Task ForceKeyframe(string displayName, CancellationToken ct)
    {
        if (this._displayStates.TryGetValue(displayName, out var state))
        {
            state.ForceNextKeyframe = true;
        }

        return Task.CompletedTask;
    }

    private sealed class DisplayState
    {
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
        public bool ForceNextKeyframe { get; set; } = true;
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI;

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

    public async Task<GrabResult> CaptureDisplay(DisplayInfo display, string? connectionId, CancellationToken ct)
    {
        var state = this.GetOrCreateDisplayState(display.Id);
        var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs || state.ForceNextKeyframe;

        foreach (var grabber in this._sortedGrabbers)
        {
            var result = await grabber.CaptureDisplay(display, keyframeDue, connectionId, ct);

            if (result.Status == GrabStatus.NoChanges)
            {
                return result;
            }
            else if (result.Status == GrabStatus.Failure)
            {
                this._logger.LogDebug("ScreenGrabber {GrabberType} failed for display {Display}, trying next", grabber.GetType().Name, display.Id);
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

    private DisplayState GetOrCreateDisplayState(string displayId)
    {
        return this._displayStates.GetOrAdd(displayId, _ => new DisplayState());
    }

    public Task ForceKeyframe(string displayId, CancellationToken ct)
    {
        if (this._displayStates.TryGetValue(displayId, out var state))
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

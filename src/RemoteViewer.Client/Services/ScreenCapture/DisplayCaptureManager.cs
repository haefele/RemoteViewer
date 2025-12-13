using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;

namespace RemoteViewer.Client.Services.ScreenCapture;

public sealed class DisplayCaptureManager(
    Connection connection,
    IDisplayService displayService,
    IScreenshotService screenshotService,
    ScreenEncoder screenEncoder,
    ILoggerFactory loggerFactory) : IDisposable
{
    private readonly Dictionary<string, DisplayCapturePipeline> _pipelines = new();
    private readonly object _pipelinesLock = new();

    private int _targetFps = 30;
    private bool _started;
    private bool _disposed;

    public int TargetFps
    {
        get => this._targetFps;
        set
        {
            if (value <= 0 || value > 120)
                throw new ArgumentOutOfRangeException(nameof(value), "FPS must be between 1 and 120");

            this._targetFps = value;
        }
    }

    public void Start()
    {
        if (this._started)
            throw new InvalidOperationException("Already started");

        this._started = true;

        connection.ViewersChanged += this.OnViewersChanged;
    }

    private void OnViewersChanged(object? sender, EventArgs e)
    {
        var displayIdsWithViewers = this.GetDisplaysWithViewers();

        lock (this._pipelinesLock)
        {
            // Stop pipelines for displays with no viewers
            var displaysToStop = this._pipelines.Keys
                .Except(displayIdsWithViewers)
                .ToList();

            foreach (var displayId in displaysToStop)
            {
                this.StopPipelineForDisplay(displayId);
            }

            // Start pipelines for displays with viewers
            foreach (var displayId in displayIdsWithViewers)
            {
                if (this._pipelines.ContainsKey(displayId) is false)
                {
                    var display = displayService.GetDisplays().FirstOrDefault(d => d.Name == displayId);
                    if (display is not null)
                    {
                        this.StartPipelineForDisplay(display);
                    }
                }
            }
        }
    }

    private HashSet<string> GetDisplaysWithViewers()
    {
        return connection.Viewers
            .Select(v => v.SelectedDisplayId)
            .Where(id => id is not null)
            .Cast<string>()
            .ToHashSet();
    }

    private void StartPipelineForDisplay(Display display)
    {
        var pipeline = new DisplayCapturePipeline(
            this,
            display,
            connection,
            screenshotService,
            screenEncoder,
            loggerFactory.CreateLogger<DisplayCapturePipeline>());

        this._pipelines[display.Name] = pipeline;
    }

    private void StopPipelineForDisplay(string displayName)
    {
        if (this._pipelines.Remove(displayName, out var pipeline))
        {
            pipeline.Dispose();
        }
    }

    public void RequestPipelineRestartForDisplay(string displayName)
    {
        // Do this in a background Task.Run, so the caller (the faulted Pipeline) is not blocked waiting for the restart to complete
        _ = Task.Run(async () =>
        {
            // Small delay to avoid rapid restart loops
            await Task.Delay(100);

            lock (this._pipelinesLock)
            {
                if (this._disposed)
                    return;

                // Stop old pipeline
                this.StopPipelineForDisplay(displayName);

                // Check if display still has viewers
                var displayNamesWithViewers = this.GetDisplaysWithViewers();
                if (displayNamesWithViewers.Contains(displayName) is false)
                    return;

                // Recreate pipeline
                var display = displayService.GetDisplays().FirstOrDefault(d => d.Name == displayName);

                if (display is not null)
                {
                    this.StartPipelineForDisplay(display);
                }
            }
        });
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        if (this._started)
        {
            connection.ViewersChanged -= this.OnViewersChanged;

            lock (this._pipelinesLock)
            {
                foreach (var pipeline in this._pipelines.Values)
                {
                    pipeline.Dispose();
                }

                this._pipelines.Clear();
            }
        }
    }
}

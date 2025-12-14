using System.Threading;
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
    private readonly Lock _pipelinesLock = new();
    private readonly CancellationTokenSource _monitorCts = new();

    private Task? _monitorTask;
    private int _targetFps = 15;
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
        this._monitorTask = Task.Run(() => this.MonitorLoopAsync(this._monitorCts.Token));
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(100, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            using (this._pipelinesLock.EnterScope())
            {
                if (this._disposed)
                    break;

                var displayIdsWithViewers = this.GetDisplaysWithViewers();

                // Stop pipelines for displays with no viewers
                var displaysToStop = this._pipelines.Keys
                    .Except(displayIdsWithViewers)
                    .ToList();

                foreach (var displayId in displaysToStop)
                {
                    this.StopPipelineForDisplay(displayId);
                }

                // Stop faulted pipelines (they will be restarted below if still needed)
                var faultedDisplays = this._pipelines
                    .Where(kv => kv.Value.IsFaulted)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var displayId in faultedDisplays)
                {
                    this.StopPipelineForDisplay(displayId);
                }

                // Start pipelines for displays with viewers (if not already running)
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
            display,
            connection,
            screenshotService,
            screenEncoder,
            () => this.TargetFps,
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

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        if (this._started)
        {
            this._monitorCts.Cancel();

            try
            {
                this._monitorTask?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
                // Ignore cancellation exceptions
            }

            using (this._pipelinesLock.EnterScope())
            {
                foreach (var pipeline in this._pipelines.Values)
                {
                    pipeline.Dispose();
                }

                this._pipelines.Clear();
            }
        }

        this._monitorCts.Dispose();
    }
}

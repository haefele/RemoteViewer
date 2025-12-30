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
    IFrameEncoder frameEncoder,
    ILoggerFactory loggerFactory,
    ILogger<DisplayCaptureManager> logger) : IDisposable
{
    private readonly Dictionary<string, DisplayCapturePipeline> _pipelines = new();
    private readonly Lock _pipelinesLock = new();

    private readonly CancellationTokenSource _monitorCts = new();
    private Task? _monitorTask;

    private int _started;
    private int _disposed;

    public int TargetFps
    {
        get;
        set
        {
            if (value <= 10 || value > 120)
                throw new ArgumentOutOfRangeException(nameof(value), "FPS must be between 1 and 120");

            field = value;
        }
    } = 15;

    public void Start()
    {
        if (Interlocked.Exchange(ref this._started, 1) == 1)
            throw new InvalidOperationException("Already started");

        logger.ManagerStarted();
        this._monitorTask = Task.Run(() => this.MonitorLoopAsync(this._monitorCts.Token));
    }

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        logger.MonitorLoopStarted();

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

            if (this._disposed == 1)
                break;

            // Get display info outside the lock to avoid holding it during external calls
            var displayIdsWithViewers = await ((IPresenterServiceImpl)connection.RequiredPresenterService).GetDisplaysWithViewers(ct);
            var availableDisplays = await displayService.GetDisplays(ct);

            using (this._pipelinesLock.EnterScope())
            {
                if (this._disposed == 1)
                    break;

                // Stop pipelines for displays with no viewers
                var displaysToStop = this._pipelines.Keys
                    .Except(displayIdsWithViewers)
                    .ToList();

                foreach (var displayId in displaysToStop)
                {
                    logger.StoppingPipeline(displayId);
                    this.StopPipelineForDisplay(displayId);
                }

                // Stop faulted pipelines (they will be restarted below if still needed)
                var faultedDisplays = this._pipelines
                    .Where(kv => kv.Value.IsFaulted)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var displayId in faultedDisplays)
                {
                    logger.FaultedPipelineDetected(displayId);
                    this.StopPipelineForDisplay(displayId);
                }

                // Start pipelines for displays with viewers (if not already running)
                foreach (var displayId in displayIdsWithViewers)
                {
                    if (this._pipelines.ContainsKey(displayId) is false)
                    {
                        var display = availableDisplays.FirstOrDefault(d => d.Name == displayId);
                        if (display is not null)
                        {
                            logger.StartingPipeline(displayId);
                            this.StartPipelineForDisplay(display);
                        }
                        else
                        {
                            logger.DisplayNotFound(displayId);
                        }
                    }
                }
            }
        }

        logger.MonitorLoopStopped();
    }

    private void StartPipelineForDisplay(Display display)
    {
        var pipeline = new DisplayCapturePipeline(
            display,
            connection,
            screenshotService,
            frameEncoder,
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
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

        if (this._started == 1)
        {
            this._monitorCts.Cancel();
            this._monitorTask?.Wait();

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
        logger.ManagerStopped();
    }
}

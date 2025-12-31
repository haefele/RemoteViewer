using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class PresenterCaptureService : IDisposable
{
    private readonly Connection _connection;
    private readonly IDisplayService _displayService;
    private readonly IScreenshotService _screenshotService;
    private readonly IFrameEncoder _frameEncoder;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PresenterCaptureService> _logger;

    private readonly Dictionary<string, DisplayCapturePipeline> _pipelines = new();
    private readonly Lock _pipelinesLock = new();
    private readonly CancellationTokenSource _monitorCts = new();
    private Task? _monitorTask;

    private int _disposed;

    public PresenterCaptureService(
        Connection connection,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        IFrameEncoder frameEncoder,
        ILoggerFactory loggerFactory,
        ILogger<PresenterCaptureService> logger)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._screenshotService = screenshotService;
        this._frameEncoder = frameEncoder;
        this._loggerFactory = loggerFactory;
        this._logger = logger;

        this._logger.ServiceStarted();
        this._monitorTask = Task.Run(() => this.MonitorLoopAsync(this._monitorCts.Token));
    }

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

    private async Task MonitorLoopAsync(CancellationToken ct)
    {
        this._logger.MonitorLoopStarted();

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
            var displayIdsWithViewers = await ((IPresenterServiceImpl)this._connection.RequiredPresenterService).GetDisplaysWithViewers(ct);
            var availableDisplays = await this._displayService.GetDisplays(this._connection.ConnectionId, ct);

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
                    this._logger.StoppingPipeline(displayId);
                    this.StopPipelineForDisplay(displayId);
                }

                // Stop faulted pipelines (they will be restarted below if still needed)
                var faultedDisplays = this._pipelines
                    .Where(kv => kv.Value.IsFaulted)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var displayId in faultedDisplays)
                {
                    this._logger.FaultedPipelineDetected(displayId);
                    this.StopPipelineForDisplay(displayId);
                }

                // Start pipelines for displays with viewers (if not already running)
                foreach (var displayId in displayIdsWithViewers)
                {
                    if (this._pipelines.ContainsKey(displayId) is false)
                    {
                        var display = availableDisplays.FirstOrDefault(d => d.Id == displayId);
                        if (display is not null)
                        {
                            this._logger.StartingPipeline(displayId);
                            this.StartPipelineForDisplay(display);
                        }
                        else
                        {
                            this._logger.DisplayNotFound(displayId);
                        }
                    }
                }
            }
        }

        this._logger.MonitorLoopStopped();
    }

    private void StartPipelineForDisplay(DisplayInfo display)
    {
        var pipeline = new DisplayCapturePipeline(
            display,
            this._connection,
            this._screenshotService,
            this._frameEncoder,
            () => this.TargetFps,
            this._loggerFactory.CreateLogger<DisplayCapturePipeline>());

        this._pipelines[display.Id] = pipeline;
    }

    private void StopPipelineForDisplay(string displayId)
    {
        if (this._pipelines.Remove(displayId, out var pipeline))
        {
            pipeline.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

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

        this._monitorCts.Dispose();
        this._logger.ServiceStopped();
    }
}

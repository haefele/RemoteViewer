using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class PresenterCaptureService : IDisposable
{
    private readonly DisplayCaptureManager _captureManager;
    private readonly ILogger<PresenterCaptureService> _logger;
    private bool _disposed;

    public PresenterCaptureService(
        Connection connection,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        IFrameEncoder frameEncoder,
        ILoggerFactory loggerFactory,
        ILogger<PresenterCaptureService> logger)
    {
        this._logger = logger;

        this._captureManager = new DisplayCaptureManager(
            connection,
            displayService,
            screenshotService,
            frameEncoder,
            loggerFactory,
            loggerFactory.CreateLogger<DisplayCaptureManager>());
        this._captureManager.Start();

        this._logger.LogInformation("Presenter capture started for connection {ConnectionId}", connection.ConnectionId);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._captureManager.Dispose();
        this._logger.LogInformation("Presenter capture stopped");
    }
}

using System;
using System.ComponentModel;
using Microsoft.Extensions.Options;

namespace RemoteViewer.DesktopDupTest;

public class TrackInputDesktopBackgroundService(ILogger<TrackInputDesktopBackgroundService> logger, IOptions<RemoteViewerOptions> remoteViewerOptions, ScreenshotService screenshotService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (remoteViewerOptions.Value.Mode is not RemoteViewerMode.SessionRecorder)
            return;

        logger.LogInformation("Starting TrackInputDesktopBackgroundService.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(0.5));
        var screenshotDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Remote Viewer", "Screenshots");
        Directory.CreateDirectory(screenshotDirectory);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Win32Helper.SwitchToInputDesktop();
                logger.LogDebug("Switched to input desktop.");

                var screenshot = await screenshotService.CaptureScreenshotAsync(stoppingToken);
                logger.LogDebug("Captured screenshot of size {Width}x{Height}.", screenshot.Width, screenshot.Height);

                screenshot.Save(Path.Combine(screenshotDirectory, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"));
                logger.LogDebug("Saved screenshot.");
            }
            catch (Win32Exception ex)
            {
                logger.LogError(ex, "Failed to switch to input desktop: {ErrorMessage}", ex.Message);
                return;
            }
        }
    }
}

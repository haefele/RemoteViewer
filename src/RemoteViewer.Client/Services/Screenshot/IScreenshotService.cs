using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.Screenshot;

public interface IScreenshotService
{
    Task<GrabResult> CaptureDisplay(DisplayInfo display, CancellationToken ct);

    Task ForceKeyframe(string displayId, CancellationToken ct);
}

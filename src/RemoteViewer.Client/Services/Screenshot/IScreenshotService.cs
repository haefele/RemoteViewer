using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.Screenshot;

public interface IScreenshotService
{
    Task<GrabResult> CaptureDisplay(DisplayInfo display, string? connectionId, CancellationToken ct);

    Task ForceKeyframe(string displayId, CancellationToken ct);
}

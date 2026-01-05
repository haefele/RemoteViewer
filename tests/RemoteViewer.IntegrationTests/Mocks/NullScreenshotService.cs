using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Shared;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullScreenshotService : IScreenshotService
{
    public Task<GrabResult> CaptureDisplay(DisplayInfo display, string? connectionId, CancellationToken ct)
        => Task.FromResult(new GrabResult(GrabStatus.NoChanges, null, null, null));

    public Task ForceKeyframe(string displayId, CancellationToken ct)
        => Task.CompletedTask;
}

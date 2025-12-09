using System.Collections.Immutable;

namespace RemoteViewer.Client.Services.ScreenCapture;

public class NullScreenshotService : IScreenshotService
{
    public bool IsSupported => false;

    public ImmutableList<Display> GetDisplays() => [];

    public GrabResult CaptureDisplay(Display display, Span<byte> targetBuffer) => new(GrabStatus.Failure, null);
}

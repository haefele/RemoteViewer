using System.Collections.Immutable;

namespace RemoteViewer.Client.Services.ScreenCapture;

/// <summary>
/// Stub implementation for non-Windows platforms. Returns empty/failure results.
/// </summary>
public class NullScreenshotService : IScreenshotService
{
    public bool IsSupported => false;

    public ImmutableList<Display> GetDisplays() => [];

    public CaptureResult CaptureDisplay(Display display) => CaptureResult.Failure;

    public void RequestKeyframe(string displayName) { }
}

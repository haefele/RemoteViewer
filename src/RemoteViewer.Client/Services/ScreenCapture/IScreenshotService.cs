using System.Collections.Immutable;
using System.Drawing;

namespace RemoteViewer.Client.Services.ScreenCapture;

public interface IScreenshotService
{
    bool IsSupported { get; }

    ImmutableList<Display> GetDisplays();

    GrabResult CaptureDisplay(Display display, Span<byte> targetBuffer);
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

public readonly record struct GrabResult(
    GrabStatus Status,
    Rectangle[]? DirtyRects
);

public enum GrabStatus
{
    Success,
    NoChanges,
    Failure
}

using System.Collections.Immutable;
using System.Drawing;
using SkiaSharp;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Service for capturing display screenshots.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Whether screen capture is supported on the current platform.
    /// </summary>
    bool IsSupported { get; }

    ImmutableList<Display> GetDisplays();
    CaptureResult CaptureDisplay(Display display);
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

public record CaptureResult(bool Success, SKBitmap? Bitmap, Rectangle[] DirtyRectangles)
{
    public static CaptureResult Failure => new(false, null, []);
    public static CaptureResult Ok(SKBitmap bitmap, Rectangle[] dirtyRectangles) => new(true, bitmap, dirtyRectangles);
    public static CaptureResult NoChanges => new(true, null, []);
}

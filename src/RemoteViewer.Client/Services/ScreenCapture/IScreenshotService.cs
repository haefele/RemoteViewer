using System.Collections.Immutable;
using System.Drawing;
using SkiaSharp;

namespace RemoteViewer.Client.Services.ScreenCapture;

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

    /// <summary>
    /// Request a keyframe for a display on the next capture cycle.
    /// Called when a new viewer selects a display to ensure they immediately receive a full frame.
    /// </summary>
    void RequestKeyframe(string displayName);
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

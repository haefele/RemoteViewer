namespace RemoteViewer.Client.Services.Screenshot;

public interface IScreenshotService
{
    Task<GrabResult> CaptureDisplay(Display display, CancellationToken ct);

    Task ForceKeyframe(string displayName, CancellationToken ct);
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

namespace RemoteViewer.Client.Services.ScreenCapture;

public interface IScreenshotService
{
    bool IsSupported { get; }

    GrabResult CaptureDisplay(Display display);

    void ForceKeyframe(string displayName);
}

public record Display(string Name, bool IsPrimary, DisplayRect Bounds);

public record struct DisplayRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

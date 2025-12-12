using RemoteViewer.Client.Common;

namespace RemoteViewer.Client.Services.ScreenCapture;

public interface IScreenGrabber
{
    bool IsAvailable { get; }
    int Priority { get; }

    GrabResult CaptureDisplay(Display display, bool forceKeyframe);
    void ResetDisplay(string displayName);
}

public readonly record struct GrabResult(
    GrabStatus Status,
    RefCountedMemoryOwner<byte>? FullFramePixels,
    DirtyRegion[]? DirtyRegions,
    MoveRegion[]? MoveRects
) : IDisposable
{
    public void Dispose()
    {
        this.FullFramePixels?.Dispose();

        if (this.DirtyRegions is not null)
        {
            foreach (var region in this.DirtyRegions)
            {
                region.Dispose();
            }
        }
    }
}

public enum GrabStatus
{
    Success,
    NoChanges,
    Failure
}

public readonly record struct DirtyRegion(
    int X,
    int Y,
    int Width,
    int Height,
    RefCountedMemoryOwner<byte> Pixels
) : IDisposable
{
    public void Dispose() => this.Pixels.Dispose();
}

public readonly record struct MoveRegion(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height
);

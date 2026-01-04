using RemoteViewer.Client.Common;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.Screenshot;

public interface IScreenGrabber
{
    bool IsAvailable { get; }
    int Priority { get; }

    Task<GrabResult> CaptureDisplay(DisplayInfo display, bool forceKeyframe, string? connectionId, CancellationToken ct);
}

public readonly record struct GrabResult(
    GrabStatus Status,
    RefCountedMemoryOwner? FullFramePixels,
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
    RefCountedMemoryOwner Pixels
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

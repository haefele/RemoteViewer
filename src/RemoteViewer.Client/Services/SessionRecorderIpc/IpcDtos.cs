using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.SessionRecorderIpc;

public sealed record DisplayDto(
    string Id,
    string FriendlyName,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom);

/// <summary>
/// Metadata for a dirty region stored in shared memory.
/// Pixels are at the specified offset in the shared buffer.
/// </summary>
public sealed record SharedRegionInfo(
    int X,
    int Y,
    int Width,
    int Height,
    int Offset)
{
    public int ByteLength => this.Width * this.Height * 4;
}

public sealed record MoveRegionDto(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);

/// <summary>
/// Result of shared memory capture.
/// All pixel data (full frame or dirty regions) is in shared memory.
/// </summary>
public sealed record SharedFrameResult(
    GrabStatus Status,
    bool HasFullFrame,
    SharedRegionInfo[]? DirtyRegions,
    MoveRegionDto[]? MoveRegions);

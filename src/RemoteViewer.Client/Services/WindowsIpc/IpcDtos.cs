using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

public sealed record DisplayDto(
    string Id,
    string FriendlyName,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom);

public sealed record GrabResultDto(
    GrabStatus Status,
    ReadOnlyMemory<byte>? FullFramePixels,
    DirtyRegionDto[]? DirtyRegions,
    MoveRegionDto[]? MoveRegions);

public sealed record DirtyRegionDto(
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

public sealed record MoveRegionDto(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);

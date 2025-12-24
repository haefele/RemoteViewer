using MessagePack;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

[MessagePackObject(keyAsPropertyName: true)]
public sealed record DisplayDto(
    string Name,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record GrabResultDto(
    GrabStatus Status,
    ReadOnlyMemory<byte>? FullFramePixels,
    DirtyRegionDto[]? DirtyRegions,
    MoveRegionDto[]? MoveRegions);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record DirtyRegionDto(
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MoveRegionDto(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);

using PolyType;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

[GenerateShape]
public sealed partial record DisplayDto(
    string Name,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom);

[GenerateShape]
public sealed partial record GrabResultDto(
    GrabStatus Status,
    ReadOnlyMemory<byte>? FullFramePixels,
    DirtyRegionDto[]? DirtyRegions,
    MoveRegionDto[]? MoveRegions);

[GenerateShape]
public sealed partial record DirtyRegionDto(
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Pixels);

[GenerateShape]
public sealed partial record MoveRegionDto(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);

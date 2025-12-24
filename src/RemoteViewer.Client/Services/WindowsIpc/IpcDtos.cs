using MessagePack;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.WindowsIpc;

// NOTE: We use byte[] instead of ReadOnlyMemory<byte> for pixel data because StreamJsonRpc's
// MessagePackFormatter (which uses MessagePack-CSharp) doesn't correctly serialize ReadOnlyMemory<byte>,
// causing data corruption and display artifacts.
// TODO: Switch back to ReadOnlyMemory<byte> when StreamJsonRpc supports Nerdbank.MessagePack,
// which handles ReadOnlyMemory<byte> correctly and avoids the extra array allocation.

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
    byte[]? FullFramePixels,
    DirtyRegionDto[]? DirtyRegions,
    MoveRegionDto[]? MoveRegions);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record DirtyRegionDto(
    int X,
    int Y,
    int Width,
    int Height,
    byte[] Pixels);

[MessagePackObject(keyAsPropertyName: true)]
public sealed record MoveRegionDto(
    int SourceX,
    int SourceY,
    int DestinationX,
    int DestinationY,
    int Width,
    int Height);

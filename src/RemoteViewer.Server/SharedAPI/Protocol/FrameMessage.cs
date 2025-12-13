using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

[GenerateShape]
public sealed partial record FrameMessage(
    string DisplayId,
    ulong FrameNumber,
    FrameCodec Codec,
    FrameRegion[] Regions
);

public enum FrameCodec : byte
{
    Jpeg90 = 0,
}

[GenerateShape]
public sealed partial record FrameRegion(
    bool IsKeyframe,
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Data
);

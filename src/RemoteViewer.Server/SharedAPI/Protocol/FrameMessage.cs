namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record FrameMessage(
    string DisplayId,
    ulong FrameNumber,
    FrameCodec Codec,
    FrameRegion[] Regions
);

public enum FrameCodec : byte
{
    Jpeg90 = 0,
}

public sealed record FrameRegion(
    bool IsKeyframe,
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Data
);

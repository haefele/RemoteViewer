using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Screen frame message containing encoded image data.
/// </summary>
/// <param name="DisplayId">Identifier of the display this frame is from</param>
/// <param name="FrameNumber">Monotonic frame sequence number for ordering</param>
/// <param name="Timestamp">Server timestamp in milliseconds</param>
/// <param name="Codec">Encoding format of the frame data</param>
/// <param name="Width">Frame width in pixels</param>
/// <param name="Height">Frame height in pixels</param>
/// <param name="Quality">Encoding quality 0-100</param>
/// <param name="Data">Encoded frame data</param>
[GenerateShape]
public sealed partial record FrameMessage(
    string DisplayId,
    ulong FrameNumber,
    long Timestamp,
    FrameCodec Codec,
    int Width,
    int Height,
    byte Quality,
    ReadOnlyMemory<byte> Data
);

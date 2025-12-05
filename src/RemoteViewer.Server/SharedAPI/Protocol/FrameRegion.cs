using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Represents a rectangular region of a frame with its encoded image data.
/// </summary>
/// <param name="X">X coordinate of the region's top-left corner</param>
/// <param name="Y">Y coordinate of the region's top-left corner</param>
/// <param name="Width">Width of the region in pixels</param>
/// <param name="Height">Height of the region in pixels</param>
/// <param name="Data">Encoded image data for this region</param>
[GenerateShape]
public sealed partial record FrameRegion(
    int X,
    int Y,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Data
);

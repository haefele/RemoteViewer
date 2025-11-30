using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Information about a single display/monitor.
/// </summary>
/// <param name="Id">Unique identifier for this display (e.g., "\\.\DISPLAY1")</param>
/// <param name="Name">Human-readable name</param>
/// <param name="IsPrimary">Whether this is the primary display</param>
/// <param name="Left">Left edge position in virtual screen coordinates</param>
/// <param name="Top">Top edge position in virtual screen coordinates</param>
/// <param name="Width">Display width in pixels</param>
/// <param name="Height">Display height in pixels</param>
[GenerateShape]
public sealed partial record DisplayInfo(
    string Id,
    string Name,
    bool IsPrimary,
    int Left,
    int Top,
    int Width,
    int Height
);

using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Mouse wheel scroll event.
/// </summary>
/// <param name="DeltaX">Horizontal scroll delta (positive = right)</param>
/// <param name="DeltaY">Vertical scroll delta (positive = up/away from user)</param>
/// <param name="X">X position as fraction of display width [0.0, 1.0]</param>
/// <param name="Y">Y position as fraction of display height [0.0, 1.0]</param>
[GenerateShape]
public sealed partial record MouseWheelMessage(
    float DeltaX,
    float DeltaY,
    float X,
    float Y
);

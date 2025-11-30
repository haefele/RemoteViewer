using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Mouse button press/release event.
/// </summary>
/// <param name="Button">Which button was pressed/released</param>
/// <param name="X">X position as fraction of display width [0.0, 1.0]</param>
/// <param name="Y">Y position as fraction of display height [0.0, 1.0]</param>
[GenerateShape]
public sealed partial record MouseButtonMessage(
    MouseButton Button,
    float X,
    float Y
);

/// <summary>
/// Mouse button identifiers.
/// </summary>
public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

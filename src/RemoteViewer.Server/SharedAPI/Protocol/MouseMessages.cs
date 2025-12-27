using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

[GenerateShape]
public sealed partial record MouseMoveMessage(float X, float Y);

[GenerateShape]
public sealed partial record MouseButtonMessage(MouseButton Button, float X, float Y);

[GenerateShape]
public sealed partial record MouseWheelMessage(float DeltaX, float DeltaY, float X, float Y);

public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

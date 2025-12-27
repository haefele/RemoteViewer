namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record MouseMoveMessage(float X, float Y);

public sealed record MouseButtonMessage(MouseButton Button, float X, float Y);

public sealed record MouseWheelMessage(float DeltaX, float DeltaY, float X, float Y);

public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

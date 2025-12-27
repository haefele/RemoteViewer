namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record KeyMessage(
    ushort KeyCode,
    KeyModifiers Modifiers
);

[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

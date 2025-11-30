using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Keyboard event message for key down/up events.
/// </summary>
/// <param name="KeyCode">Windows virtual key code (VK_*)</param>
/// <param name="ScanCode">Hardware scan code</param>
/// <param name="Modifiers">Active modifier keys at time of event</param>
/// <param name="IsExtendedKey">True if extended key (e.g., right Ctrl/Alt, numpad Enter)</param>
[GenerateShape]
public sealed partial record KeyMessage(
    ushort KeyCode,
    ushort ScanCode,
    KeyModifiers Modifiers,
    bool IsExtendedKey = false
);

/// <summary>
/// Modifier key flags. Can be combined.
/// </summary>
[Flags]
public enum KeyModifiers : byte
{
    None = 0,
    Shift = 1 << 0,
    Control = 1 << 1,
    Alt = 1 << 2,
    Win = 1 << 3,
}

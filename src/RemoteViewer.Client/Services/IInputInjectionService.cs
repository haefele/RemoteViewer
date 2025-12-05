using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Service for injecting mouse and keyboard input on the presenter machine.
/// </summary>
public interface IInputInjectionService
{
    /// <summary>
    /// Injects a mouse move event at the specified normalized coordinates on the given display.
    /// </summary>
    void InjectMouseMove(Display display, float normalizedX, float normalizedY);

    /// <summary>
    /// Injects a mouse button down or up event.
    /// </summary>
    void InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY);

    /// <summary>
    /// Injects a mouse wheel scroll event.
    /// </summary>
    void InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY);

    /// <summary>
    /// Injects a key down or up event using the Windows virtual key code.
    /// </summary>
    void InjectKey(ushort keyCode, bool isDown);

    /// <summary>
    /// Releases all currently tracked modifier keys (Shift, Ctrl, Alt, Win).
    /// Call this when a viewer disconnects to prevent stuck modifiers.
    /// </summary>
    void ReleaseAllModifiers();
}

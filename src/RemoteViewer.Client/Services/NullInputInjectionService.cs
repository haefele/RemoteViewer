using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Stub implementation for non-Windows platforms. All methods are no-ops.
/// </summary>
public class NullInputInjectionService : IInputInjectionService
{
    public void InjectMouseMove(Display display, float normalizedX, float normalizedY) { }

    public void InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY) { }

    public void InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY) { }

    public void InjectKey(ushort keyCode, bool isDown) { }

    public void ReleaseAllModifiers() { }
}

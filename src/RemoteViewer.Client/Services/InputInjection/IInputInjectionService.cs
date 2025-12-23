using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.InputInjection;

public interface IInputInjectionService
{
    Task InjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct);

    Task ReleaseAllModifiers(CancellationToken ct);
}

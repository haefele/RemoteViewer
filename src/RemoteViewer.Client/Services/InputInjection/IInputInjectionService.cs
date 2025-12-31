using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.InputInjection;

public interface IInputInjectionService
{
    Task InjectMouseMove(DisplayInfo display, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectMouseButton(DisplayInfo display, MouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectMouseWheel(DisplayInfo display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct);

    Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct);

    Task ReleaseAllModifiers(CancellationToken ct);
}

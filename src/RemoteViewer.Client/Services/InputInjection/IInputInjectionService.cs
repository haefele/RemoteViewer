using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.InputInjection;

public interface IInputInjectionService
{
    Task InjectMouseMove(DisplayInfo display, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct);

    Task InjectMouseButton(DisplayInfo display, MouseButton button, bool isDown, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct);

    Task InjectMouseWheel(DisplayInfo display, float deltaX, float deltaY, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct);

    Task InjectKey(ushort keyCode, bool isDown, string? connectionId, CancellationToken ct);

    Task ReleaseAllModifiers(string? connectionId, CancellationToken ct);
}

using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.InputInjection;

public class NullInputInjectionService : IInputInjectionService
{
    public Task InjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct)
        => Task.CompletedTask;

    public Task InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
        => Task.CompletedTask;

    public Task InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
        => Task.CompletedTask;

    public Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct)
        => Task.CompletedTask;

    public Task ReleaseAllModifiers(CancellationToken ct)
        => Task.CompletedTask;
}

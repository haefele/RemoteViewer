using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsIpcInputInjectionService(SessionRecorderRpcClient rpcClient) : IInputInjectionService
{
    public async Task InjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
        await proxy.InjectMouseMove(display.Name, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
        await proxy.InjectMouseButton(display.Name, (int)button, isDown, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
        await proxy.InjectMouseWheel(display.Name, deltaX, deltaY, normalizedX, normalizedY, ct);
    }

    public async Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
        await proxy.InjectKey(keyCode, isDown, ct);
    }

    public async Task ReleaseAllModifiers(CancellationToken ct)
    {
        var proxy = rpcClient.Proxy ?? throw new InvalidOperationException("Not connected to SessionRecorder service");
        await proxy.ReleaseAllModifiers(ct);
    }
}

using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class WindowsHybridInputInjectionService(
    WindowsInputInjectionService localService,
    SessionRecorderRpcClient rpcClient,
    ILogger<WindowsHybridInputInjectionService> logger) : IInputInjectionService
{
    public Task InjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => rpcClient.Proxy!.InjectMouseMove(display.Name, normalizedX, normalizedY, ct),
            () => localService.InjectMouseMove(display, normalizedX, normalizedY, ct),
            "inject mouse move");

    public Task InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => rpcClient.Proxy!.InjectMouseButton(display.Name, (int)button, isDown, normalizedX, normalizedY, ct),
            () => localService.InjectMouseButton(display, button, isDown, normalizedX, normalizedY, ct),
            "inject mouse button");

    public Task InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => rpcClient.Proxy!.InjectMouseWheel(display.Name, deltaX, deltaY, normalizedX, normalizedY, ct),
            () => localService.InjectMouseWheel(display, deltaX, deltaY, normalizedX, normalizedY, ct),
            "inject mouse wheel");

    public Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => rpcClient.Proxy!.InjectKey(keyCode, isDown, ct),
            () => localService.InjectKey(keyCode, isDown, ct),
            "inject key");

    public Task ReleaseAllModifiers(CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => rpcClient.Proxy!.ReleaseAllModifiers(ct),
            () => localService.ReleaseAllModifiers(ct),
            "release modifiers");

    private async Task ExecuteWithFallbackAsync(Func<Task> ipcAction, Func<Task> localAction, string operationName)
    {
        if (rpcClient.IsConnected)
        {
            try
            {
                await ipcAction();
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to {OperationName} via IPC, falling back to local service", operationName);
            }
        }

        await localAction();
    }
}

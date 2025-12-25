using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsSession;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class SessionRecorderRpcServer(
    IWin32SessionService win32SessionService,
    WindowsDisplayService displayService,
    IScreenshotService screenshotService,
    WindowsInputInjectionService inputInjectionService) : ISessionRecorderRpc
{
    public async Task<DisplayDto[]> GetDisplays(CancellationToken ct)
    {
        var displays = await displayService.GetDisplays(ct);
        return displays.Select(d => d.ToDto()).ToArray();
    }

    public async Task<GrabResultDto> CaptureDisplay(string displayName, bool forceKeyframe, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayName, ct);
        if (display is null)
        {
            return new GrabResultDto(GrabStatus.Failure, null, null, null);
        }

        if (forceKeyframe)
        {
            await screenshotService.ForceKeyframe(displayName, ct);
        }

        var result = await screenshotService.CaptureDisplay(display, ct);
        try
        {
            return result.ToDto();
        }
        finally
        {
            result.Dispose();
        }
    }

    public async Task InjectMouseMove(string displayName, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayName, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseMove(display, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseButton(string displayName, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayName, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseButton(display, (MouseButton)button, isDown, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseWheel(string displayName, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayName, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseWheel(display, deltaX, deltaY, normalizedX, normalizedY, ct);
    }

    public Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();
        return inputInjectionService.InjectKey(keyCode, isDown, ct);
    }

    public Task ReleaseAllModifiers(CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();
        return inputInjectionService.ReleaseAllModifiers(ct);
    }

    private async Task<Display?> ResolveDisplayAsync(string displayName, CancellationToken ct)
    {
        var displays = await displayService.GetDisplays(ct);
        return displays.FirstOrDefault(d => d.Name == displayName);
    }
}

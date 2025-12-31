using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsSession;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;
using Windows.Win32;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class SessionRecorderRpcServer(
    IWin32SessionService win32SessionService,
    IDisplayService displayService,
    IScreenshotService screenshotService,
    IInputInjectionService inputInjectionService,
    ILogger<SessionRecorderRpcServer> logger) : ISessionRecorderRpc
{
    public async Task<DisplayDto[]> GetDisplays(CancellationToken ct)
    {
        var displays = await displayService.GetDisplays(ct);
        return displays.Select(d => d.ToIpcDto()).ToArray();
    }

    public async Task<GrabResultDto> CaptureDisplay(string displayId, bool forceKeyframe, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null)
        {
            return new GrabResultDto(GrabStatus.Failure, null, null, null);
        }

        if (forceKeyframe)
        {
            await screenshotService.ForceKeyframe(displayId, ct);
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

    public async Task InjectMouseMove(string displayId, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseMove(display, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseButton(string displayId, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseButton(display, (MouseButton)button, isDown, normalizedX, normalizedY, ct);
    }

    public async Task InjectMouseWheel(string displayId, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
    {
        win32SessionService.SwitchToInputDesktop();

        var display = await this.ResolveDisplayAsync(displayId, ct);
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

    public Task<bool> SendSecureAttentionSequence(CancellationToken ct)
    {
        try
        {
            PInvoke.SendSAS(AsUser: true);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SendSAS failed with exception");
            return Task.FromResult(false);
        }
    }

    private async Task<DisplayInfo?> ResolveDisplayAsync(string displayId, CancellationToken ct)
    {
        var displays = await displayService.GetDisplays(ct);
        return displays.FirstOrDefault(d => d.Id == displayId);
    }
}

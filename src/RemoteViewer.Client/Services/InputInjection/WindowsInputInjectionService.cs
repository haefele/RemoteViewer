#if WINDOWS
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.SessionRecorderIpc;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;
using Windows.Win32;
using WindowsInput;

using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;

namespace RemoteViewer.Client.Services.InputInjection;

public class WindowsInputInjectionService : IInputInjectionService
{
    private static readonly TimeSpan s_modifierTimeout = TimeSpan.FromSeconds(10);

    private readonly SessionRecorderRpcClient? _rpcClient;
    private readonly ILogger<WindowsInputInjectionService> _logger;
    private readonly InputSimulator _simulator = new();

    private readonly ConcurrentDictionary<VirtualKeyCode, DateTime> _pressedModifiers = new();
    private DateTime _lastInputTime = DateTime.UtcNow;

    private float _verticalScrollAccumulator;
    private float _horizontalScrollAccumulator;

    public WindowsInputInjectionService(
        SessionRecorderRpcClient? rpcClient,
        ILogger<WindowsInputInjectionService> logger)
    {
        this._rpcClient = rpcClient;
        this._logger = logger;
    }

    public Task InjectMouseMove(DisplayInfo display, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            connectionId,
            cid => this._rpcClient!.Proxy!.InjectMouseMove(cid, display.Id, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseMove(display, normalizedX, normalizedY, ct),
            "inject mouse move");

    public Task InjectMouseButton(DisplayInfo display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            connectionId,
            cid => this._rpcClient!.Proxy!.InjectMouseButton(cid, display.Id, (int)button, isDown, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseButton(display, button, isDown, normalizedX, normalizedY, ct),
            "inject mouse button");

    public Task InjectMouseWheel(DisplayInfo display, float deltaX, float deltaY, float normalizedX, float normalizedY, string? connectionId, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            connectionId,
            cid => this._rpcClient!.Proxy!.InjectMouseWheel(cid, display.Id, deltaX, deltaY, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseWheel(display, deltaX, deltaY, normalizedX, normalizedY, ct),
            "inject mouse wheel");

    public Task InjectKey(ushort keyCode, bool isDown, string? connectionId, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            connectionId,
            cid => this._rpcClient!.Proxy!.InjectKey(cid, keyCode, isDown, ct),
            () => this.ActualInjectKey(keyCode, isDown, ct),
            "inject key");

    public Task ReleaseAllModifiers(string? connectionId, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            connectionId,
            cid => this._rpcClient!.Proxy!.ReleaseAllModifiers(cid, ct),
            () => this.ActualReleaseAllModifiers(ct),
            "release modifiers");

    private Task ActualInjectMouseMove(DisplayInfo display, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.CheckAndReleaseStuckModifiers();

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        return Task.CompletedTask;
    }

    private Task ActualInjectMouseButton(DisplayInfo display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.CheckAndReleaseStuckModifiers();
        this._lastInputTime = DateTime.UtcNow;

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        switch (button)
        {
            case ProtocolMouseButton.Left:
                if (isDown)
                    this._simulator.Mouse.LeftButtonDown();
                else
                    this._simulator.Mouse.LeftButtonUp();
                break;
            case ProtocolMouseButton.Right:
                if (isDown)
                    this._simulator.Mouse.RightButtonDown();
                else
                    this._simulator.Mouse.RightButtonUp();
                break;
            case ProtocolMouseButton.Middle:
                if (isDown)
                    this._simulator.Mouse.MiddleButtonDown();
                else
                    this._simulator.Mouse.MiddleButtonUp();
                break;
            case ProtocolMouseButton.XButton1:
                if (isDown)
                    this._simulator.Mouse.XButtonDown(1);
                else
                    this._simulator.Mouse.XButtonUp(1);
                break;
            case ProtocolMouseButton.XButton2:
                if (isDown)
                    this._simulator.Mouse.XButtonDown(2);
                else
                    this._simulator.Mouse.XButtonUp(2);
                break;
            default:
                this._logger.LogWarning("Unknown mouse button: {Button}", button);
                break;
        }

        return Task.CompletedTask;
    }

    private Task ActualInjectMouseWheel(DisplayInfo display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.CheckAndReleaseStuckModifiers();
        this._lastInputTime = DateTime.UtcNow;

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        this._verticalScrollAccumulator += deltaY;
        var verticalClicks = (int)this._verticalScrollAccumulator;
        if (verticalClicks != 0)
        {
            this._simulator.Mouse.VerticalScroll(verticalClicks);
            this._verticalScrollAccumulator -= verticalClicks;
        }

        this._horizontalScrollAccumulator += deltaX;
        var horizontalClicks = (int)this._horizontalScrollAccumulator;
        if (horizontalClicks != 0)
        {
            this._simulator.Mouse.HorizontalScroll(horizontalClicks);
            this._horizontalScrollAccumulator -= horizontalClicks;
        }

        return Task.CompletedTask;
    }

    private Task ActualInjectKey(ushort keyCode, bool isDown, CancellationToken ct)
    {
        this.CheckAndReleaseStuckModifiers();
        this._lastInputTime = DateTime.UtcNow;

        var vk = (VirtualKeyCode)keyCode;

        if (IsModifierKey(vk))
        {
            if (isDown)
            {
                this._pressedModifiers[vk] = DateTime.UtcNow;
            }
            else
            {
                this._pressedModifiers.TryRemove(vk, out _);
            }
        }

        if (isDown)
        {
            this._simulator.Keyboard.KeyDown(vk);
        }
        else
        {
            this._simulator.Keyboard.KeyUp(vk);
        }

        return Task.CompletedTask;
    }

    private Task ActualReleaseAllModifiers(CancellationToken ct)
    {
        foreach (var vk in this._pressedModifiers.Keys)
        {
            this._logger.LogInformation("Releasing modifier key on cleanup: {Key}", vk);
            this._simulator.Keyboard.KeyUp(vk);
        }
        this._pressedModifiers.Clear();

        return Task.CompletedTask;
    }

    private async Task ExecuteWithFallbackAsync(string? connectionId, Func<string, Task> ipcAction, Func<Task> localAction, string operationName)
    {
        if (connectionId is not null && this._rpcClient is not null && this._rpcClient.IsConnected && this._rpcClient.IsAuthenticatedFor(connectionId))
        {
            try
            {
                await ipcAction(connectionId);
                return;
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to {OperationName} via IPC, falling back to local service", operationName);
            }
        }

        await localAction();
    }

    private static bool IsModifierKey(VirtualKeyCode vk) => vk is
        VirtualKeyCode.LSHIFT or VirtualKeyCode.RSHIFT or
        VirtualKeyCode.LCONTROL or VirtualKeyCode.RCONTROL or
        VirtualKeyCode.LMENU or VirtualKeyCode.RMENU or
        VirtualKeyCode.LWIN or VirtualKeyCode.RWIN;

    private void CheckAndReleaseStuckModifiers()
    {
        var now = DateTime.UtcNow;
        var timeSinceLastInput = now - this._lastInputTime;

        if (timeSinceLastInput < s_modifierTimeout)
            return;

        if (this._pressedModifiers.IsEmpty)
            return;

        foreach (var (vk, pressedTime) in this._pressedModifiers)
        {
            if (now - pressedTime >= s_modifierTimeout)
            {
                this._logger.LogWarning("Auto-releasing stuck modifier key: {Key} (held for {Duration:F1}s)", vk, (now - pressedTime).TotalSeconds);
                this._simulator.Keyboard.KeyUp(vk);
                this._pressedModifiers.TryRemove(vk, out _);
            }
        }
    }

    private static (int absX, int absY) NormalizedToAbsolute(DisplayInfo display, float normalizedX, float normalizedY)
    {
        var virtualLeft = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var virtualTop = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var virtualWidth = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var virtualHeight = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        var screenX = display.Left + (int)(normalizedX * display.Width);
        var screenY = display.Top + (int)(normalizedY * display.Height);

        var absX = ((screenX - virtualLeft) * 65535) / virtualWidth;
        var absY = ((screenY - virtualTop) * 65535) / virtualHeight;

        return (absX, absY);
    }
}
#endif

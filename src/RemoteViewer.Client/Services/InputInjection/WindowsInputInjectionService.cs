#if WINDOWS
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsIpc;
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

    public Task InjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => this._rpcClient!.Proxy!.InjectMouseMove(display.Name, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseMove(display, normalizedX, normalizedY, ct),
            "inject mouse move");

    public Task InjectMouseButton(Display display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => this._rpcClient!.Proxy!.InjectMouseButton(display.Name, (int)button, isDown, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseButton(display, button, isDown, normalizedX, normalizedY, ct),
            "inject mouse button");

    public Task InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => this._rpcClient!.Proxy!.InjectMouseWheel(display.Name, deltaX, deltaY, normalizedX, normalizedY, ct),
            () => this.ActualInjectMouseWheel(display, deltaX, deltaY, normalizedX, normalizedY, ct),
            "inject mouse wheel");

    public Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => this._rpcClient!.Proxy!.InjectKey(keyCode, isDown, ct),
            () => this.ActualInjectKey(keyCode, isDown, ct),
            "inject key");

    public Task ReleaseAllModifiers(CancellationToken ct)
        => this.ExecuteWithFallbackAsync(
            () => this._rpcClient!.Proxy!.ReleaseAllModifiers(ct),
            () => this.ActualReleaseAllModifiers(ct),
            "release modifiers");

    private Task ActualInjectMouseMove(Display display, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.CheckAndReleaseStuckModifiers();

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        return Task.CompletedTask;
    }

    private Task ActualInjectMouseButton(Display display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
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
            default:
                this._logger.LogWarning("Unknown mouse button: {Button}", button);
                break;
        }

        return Task.CompletedTask;
    }

    private Task ActualInjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
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

    private async Task ExecuteWithFallbackAsync(Func<Task> ipcAction, Func<Task> localAction, string operationName)
    {
        if (this._rpcClient?.IsConnected == true)
        {
            try
            {
                await ipcAction();
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

    private static (int absX, int absY) NormalizedToAbsolute(Display display, float normalizedX, float normalizedY)
    {
        var virtualLeft = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var virtualTop = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var virtualWidth = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var virtualHeight = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        var screenX = display.Bounds.Left + (int)(normalizedX * display.Bounds.Width);
        var screenY = display.Bounds.Top + (int)(normalizedY * display.Bounds.Height);

        var absX = ((screenX - virtualLeft) * 65535) / virtualWidth;
        var absY = ((screenY - virtualTop) * 65535) / virtualHeight;

        return (absX, absY);
    }
}
#endif

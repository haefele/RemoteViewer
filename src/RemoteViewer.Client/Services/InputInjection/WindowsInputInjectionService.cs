#if WINDOWS
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;
using Windows.Win32;
using WindowsInput;

using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;

namespace RemoteViewer.Client.Services.InputInjection;

/// <summary>
/// Service for injecting mouse and keyboard input on the presenter machine.
/// Uses H.InputSimulator for input simulation.
/// </summary>
public class WindowsInputInjectionService : IInputInjectionService
{
    private static readonly TimeSpan s_modifierTimeout = TimeSpan.FromSeconds(10);

    private readonly ILogger<WindowsInputInjectionService> _logger;
    private readonly InputSimulator _simulator = new();

    // Track which modifier keys are currently pressed and when they were pressed
    private readonly ConcurrentDictionary<VirtualKeyCode, DateTime> _pressedModifiers = new();
    private DateTime _lastInputTime = DateTime.UtcNow;

    // Accumulators for fractional scroll values (high-precision/smooth scrolling)
    private float _verticalScrollAccumulator;
    private float _horizontalScrollAccumulator;

    public WindowsInputInjectionService(ILogger<WindowsInputInjectionService> logger)
    {
        this._logger = logger;
    }

    /// <summary>
    /// Injects a mouse move event at the specified normalized coordinates on the given display.
    /// </summary>
    public void InjectMouseMove(Display display, float normalizedX, float normalizedY)
    {
        this.CheckAndReleaseStuckModifiers();

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);
    }

    /// <summary>
    /// Injects a mouse button down or up event.
    /// </summary>
    public void InjectMouseButton(Display display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY)
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
    }

    /// <summary>
    /// Injects a mouse wheel scroll event.
    /// </summary>
    public void InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY)
    {
        this.CheckAndReleaseStuckModifiers();
        this._lastInputTime = DateTime.UtcNow;

        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        // Accumulate vertical scroll and dispatch whole clicks
        this._verticalScrollAccumulator += deltaY;
        var verticalClicks = (int)this._verticalScrollAccumulator;
        if (verticalClicks != 0)
        {
            this._simulator.Mouse.VerticalScroll(verticalClicks);
            this._verticalScrollAccumulator -= verticalClicks;
        }

        // Accumulate horizontal scroll and dispatch whole clicks
        this._horizontalScrollAccumulator += deltaX;
        var horizontalClicks = (int)this._horizontalScrollAccumulator;
        if (horizontalClicks != 0)
        {
            this._simulator.Mouse.HorizontalScroll(horizontalClicks);
            this._horizontalScrollAccumulator -= horizontalClicks;
        }
    }

    /// <summary>
    /// Injects a key down or up event using the Windows virtual key code.
    /// </summary>
    public void InjectKey(ushort keyCode, bool isDown)
    {
        this.CheckAndReleaseStuckModifiers();
        this._lastInputTime = DateTime.UtcNow;

        var vk = (VirtualKeyCode)keyCode;

        // Track modifier key state
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
    }

    /// <summary>
    /// Releases all currently tracked modifier keys.
    /// Call this when a viewer disconnects to prevent stuck modifiers.
    /// </summary>
    public void ReleaseAllModifiers()
    {
        foreach (var vk in this._pressedModifiers.Keys)
        {
            this._logger.LogInformation("Releasing modifier key on cleanup: {Key}", vk);
            this._simulator.Keyboard.KeyUp(vk);
        }
        this._pressedModifiers.Clear();
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

        // Only check if there's been no meaningful input for the timeout period
        if (timeSinceLastInput < s_modifierTimeout)
            return;

        if (this._pressedModifiers.IsEmpty)
            return;

        // Release any modifiers that have been held too long
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

    /// <summary>
    /// Converts normalized coordinates (0-1) to absolute coordinates for SendInput.
    /// SendInput uses 0-65535 range for the entire virtual desktop.
    /// </summary>
    private static (int absX, int absY) NormalizedToAbsolute(Display display, float normalizedX, float normalizedY)
    {
        // Get virtual desktop dimensions
        var virtualLeft = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        var virtualTop = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        var virtualWidth = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        var virtualHeight = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        // Calculate actual screen position
        var screenX = display.Bounds.Left + (int)(normalizedX * display.Bounds.Width);
        var screenY = display.Bounds.Top + (int)(normalizedY * display.Bounds.Height);

        // Convert to virtual desktop 0-65535 range
        var absX = ((screenX - virtualLeft) * 65535) / virtualWidth;
        var absY = ((screenY - virtualTop) * 65535) / virtualHeight;

        return (absX, absY);
    }
}
#endif

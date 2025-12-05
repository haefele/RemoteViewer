#if WINDOWS
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI.Protocol;
using Windows.Win32;
using WindowsInput;

using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;

namespace RemoteViewer.Client.Services.Windows;

/// <summary>
/// Service for injecting mouse and keyboard input on the presenter machine.
/// Uses H.InputSimulator for input simulation.
/// </summary>
public class InputInjectionService : IInputInjectionService
{
    private readonly ILogger<InputInjectionService> _logger;
    private readonly InputSimulator _simulator = new();

    public InputInjectionService(ILogger<InputInjectionService> logger)
    {
        this._logger = logger;
    }

    /// <summary>
    /// Injects a mouse move event at the specified normalized coordinates on the given display.
    /// </summary>
    public void InjectMouseMove(Display display, float normalizedX, float normalizedY)
    {
        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);
    }

    /// <summary>
    /// Injects a mouse button down or up event.
    /// </summary>
    public void InjectMouseButton(Display display, ProtocolMouseButton button, bool isDown, float normalizedX, float normalizedY)
    {
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
        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);
        this._simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);

        // Vertical scroll
        if (Math.Abs(deltaY) > 0.001f)
        {
            this._simulator.Mouse.VerticalScroll((int)deltaY);
        }

        // Horizontal scroll
        if (Math.Abs(deltaX) > 0.001f)
        {
            this._simulator.Mouse.HorizontalScroll((int)deltaX);
        }
    }

    /// <summary>
    /// Injects a key down or up event using the Windows virtual key code.
    /// </summary>
    public void InjectKey(ushort keyCode, bool isDown)
    {
        var vk = (VirtualKeyCode)keyCode;
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

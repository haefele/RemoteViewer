#if WINDOWS
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI.Protocol;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RemoteViewer.Client.Services.Windows;

/// <summary>
/// Service for injecting mouse and keyboard input on the presenter machine.
/// Uses Win32 SendInput API to simulate user input.
/// </summary>
public class InputInjectionService : IInputInjectionService
{
    private readonly ILogger<InputInjectionService> _logger;

    public InputInjectionService(ILogger<InputInjectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Injects a mouse move event at the specified normalized coordinates on the given display.
    /// </summary>
    public void InjectMouseMove(Display display, float normalizedX, float normalizedY)
    {
        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);

        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK
                }
            }
        };

        SendInputSafe(input);
    }

    /// <summary>
    /// Injects a mouse button down or up event.
    /// </summary>
    public void InjectMouseButton(Display display, MouseButton button, bool isDown, float normalizedX, float normalizedY)
    {
        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);

        var flags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK;
        flags |= GetMouseButtonFlag(button, isDown);

        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                mi = new MOUSEINPUT
                {
                    dx = absX,
                    dy = absY,
                    dwFlags = flags
                }
            }
        };

        SendInputSafe(input);
    }

    /// <summary>
    /// Injects a mouse wheel scroll event.
    /// </summary>
    public void InjectMouseWheel(Display display, float deltaX, float deltaY, float normalizedX, float normalizedY)
    {
        var (absX, absY) = NormalizedToAbsolute(display, normalizedX, normalizedY);

        // Vertical scroll
        if (Math.Abs(deltaY) > 0.001f)
        {
            var input = new INPUT
            {
                type = INPUT_TYPE.INPUT_MOUSE,
                Anonymous = new INPUT._Anonymous_e__Union
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        mouseData = (uint)(int)(deltaY * 120), // WHEEL_DELTA = 120
                        dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK
                    }
                }
            };

            SendInputSafe(input);
        }

        // Horizontal scroll
        if (Math.Abs(deltaX) > 0.001f)
        {
            var input = new INPUT
            {
                type = INPUT_TYPE.INPUT_MOUSE,
                Anonymous = new INPUT._Anonymous_e__Union
                {
                    mi = new MOUSEINPUT
                    {
                        dx = absX,
                        dy = absY,
                        mouseData = (uint)(int)(deltaX * 120),
                        dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK
                    }
                }
            };

            SendInputSafe(input);
        }
    }

    /// <summary>
    /// Injects a key down or up event.
    /// </summary>
    public void InjectKey(ushort keyCode, ushort scanCode, bool isDown, bool isExtended)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_SCANCODE;
        if (!isDown)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        }
        if (isExtended)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = INPUT_TYPE.INPUT_KEYBOARD,
            Anonymous = new INPUT._Anonymous_e__Union
            {
                ki = new KEYBDINPUT
                {
                    wVk = (VIRTUAL_KEY)keyCode,
                    wScan = scanCode,
                    dwFlags = flags
                }
            }
        };

        SendInputSafe(input);
    }

    private static MOUSE_EVENT_FLAGS GetMouseButtonFlag(MouseButton button, bool isDown) => button switch
    {
        MouseButton.Left => isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_LEFTUP,
        MouseButton.Right => isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_RIGHTUP,
        MouseButton.Middle => isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    /// <summary>
    /// Converts normalized coordinates (0-1) to absolute coordinates for SendInput.
    /// SendInput uses 0-65535 range for the entire virtual desktop.
    /// </summary>
    private static (int absX, int absY) NormalizedToAbsolute(Display display, float normalizedX, float normalizedY)
    {
        // Get virtual desktop dimensions
        int virtualLeft = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        int virtualTop = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        int virtualWidth = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN);
        int virtualHeight = PInvoke.GetSystemMetrics(global::Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN);

        // Calculate actual screen position
        int screenX = display.Bounds.Left + (int)(normalizedX * display.Bounds.Width);
        int screenY = display.Bounds.Top + (int)(normalizedY * display.Bounds.Height);

        // Convert to virtual desktop 0-65535 range
        int absX = ((screenX - virtualLeft) * 65535) / virtualWidth;
        int absY = ((screenY - virtualTop) * 65535) / virtualHeight;

        return (absX, absY);
    }

    private unsafe void SendInputSafe(INPUT input)
    {
        var inputs = stackalloc INPUT[1];
        inputs[0] = input;

        uint result = PInvoke.SendInput(1, inputs, sizeof(INPUT));
        if (result == 0)
        {
            _logger.LogWarning("SendInput failed");
        }
    }
}
#endif

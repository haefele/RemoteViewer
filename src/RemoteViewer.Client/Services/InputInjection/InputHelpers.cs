#if WINDOWS
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace RemoteViewer.Client.Services.InputInjection;

internal static class InputHelpers
{
    public static void SendMouseMove(int absX, int absY)
    {
        var input = CreateMouseInput(
            absX,
            absY,
            MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE |
            MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE |
            MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK);

        SendSingleInput(input);
    }

    public static void SendMouseButton(MOUSE_EVENT_FLAGS flags, uint mouseData = 0)
    {
        var input = CreateMouseInput(0, 0, flags, mouseData);
        SendSingleInput(input);
    }

    public static void SendMouseWheel(int scrollAmount, bool horizontal)
    {
        var flags = horizontal
            ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_HWHEEL
            : MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL;

        var input = CreateMouseInput(0, 0, flags, (uint)scrollAmount);
        SendSingleInput(input);
    }

    public static void SendKeyDown(ushort vk)
    {
        var flags = IsExtendedKey(vk) ? KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY : 0;
        var input = CreateKeyboardInput(vk, flags);
        SendSingleInput(input);
    }

    public static void SendKeyUp(ushort vk)
    {
        var flags = KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        if (IsExtendedKey(vk))
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;

        var input = CreateKeyboardInput(vk, flags);
        SendSingleInput(input);
    }

    public static void SendUnicodeText(string text)
    {
        if (text.Length == 0)
            return;

        var inputs = new INPUT[text.Length * 2];
        var index = 0;

        foreach (var c in text)
        {
            inputs[index++] = CreateUnicodeInput(c, 0);
            inputs[index++] = CreateUnicodeInput(c, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP);
        }

        SendInputs(inputs);
    }

    private static INPUT CreateMouseInput(int absX, int absY, MOUSE_EVENT_FLAGS flags, uint mouseData = 0)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.Anonymous.mi = new MOUSEINPUT
        {
            dx = absX,
            dy = absY,
            mouseData = mouseData,
            dwFlags = flags,
            time = 0,
            dwExtraInfo = nuint.Zero
        };
        return input;
    }

    private static INPUT CreateKeyboardInput(ushort vk, KEYBD_EVENT_FLAGS flags)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT
        {
            wVk = (VIRTUAL_KEY)vk,
            wScan = (ushort)(PInvoke.MapVirtualKey(vk, 0) & 0xFFu),
            dwFlags = flags,
            time = 0,
            dwExtraInfo = nuint.Zero
        };
        return input;
    }

    private static INPUT CreateUnicodeInput(char c, KEYBD_EVENT_FLAGS flags)
    {
        ushort scanCode = c;

        // Handle extended keys: if scan code has 0xE0 prefix
        if ((scanCode & 0xFF00) == 0xE000)
        {
            flags |= KEYBD_EVENT_FLAGS.KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.Anonymous.ki = new KEYBDINPUT
        {
            wVk = 0,
            wScan = scanCode,
            dwFlags = flags | KEYBD_EVENT_FLAGS.KEYEVENTF_UNICODE,
            time = 0,
            dwExtraInfo = nuint.Zero
        };
        return input;
    }

    private static unsafe void SendSingleInput(INPUT input)
    {
        PInvoke.SendInput(new ReadOnlySpan<INPUT>(ref input), sizeof(INPUT));
    }

    private static unsafe void SendInputs(INPUT[] inputs)
    {
        PInvoke.SendInput(inputs, sizeof(INPUT));
    }

    private static bool IsExtendedKey(ushort vk) => vk is
        0x03 or // VK_CANCEL (Break)
        0x11 or // VK_CONTROL (generic)
        0x12 or // VK_MENU (generic Alt)
        0x21 or // VK_PRIOR (Page Up)
        0x22 or // VK_NEXT (Page Down)
        0x23 or // VK_END
        0x24 or // VK_HOME
        0x25 or // VK_LEFT
        0x26 or // VK_UP
        0x27 or // VK_RIGHT
        0x28 or // VK_DOWN
        0x2C or // VK_SNAPSHOT (Print Screen)
        0x2D or // VK_INSERT
        0x2E or // VK_DELETE
        0x6F or // VK_DIVIDE (Numpad /)
        0x90 or // VK_NUMLOCK
        0xA3 or // VK_RCONTROL
        0xA5;   // VK_RMENU (Right Alt)
}
#endif

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Server.SharedAPI.Protocol.KeyModifiers;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerView : Window
{
    private ViewerViewModel? _viewModel;

    public ViewerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = DataContext as ViewerViewModel;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _viewModel?.Dispose();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_viewModel is null)
            return;

        var (x, y) = GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            _viewModel.SendMouseMove(x, y);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_viewModel is null)
            return;

        var (x, y) = GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            var point = e.GetCurrentPoint(FrameImage);
            var button = GetMouseButton(point.Properties);
            if (button is not null)
            {
                _viewModel.SendMouseDown(button.Value, x, y);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_viewModel is null)
            return;

        var (x, y) = GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Left => ProtocolMouseButton.Left,
                MouseButton.Right => ProtocolMouseButton.Right,
                MouseButton.Middle => ProtocolMouseButton.Middle,
                _ => (ProtocolMouseButton?)null
            };

            if (button is not null)
            {
                _viewModel.SendMouseUp(button.Value, x, y);
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_viewModel is null)
            return;

        var (x, y) = GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            _viewModel.SendMouseWheel((float)e.Delta.X, (float)e.Delta.Y, x, y);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_viewModel is null)
            return;

        var (keyCode, scanCode, isExtended) = GetKeyInfo(e);
        var modifiers = GetKeyModifiers(e.KeyModifiers);
        _viewModel.SendKeyDown(keyCode, scanCode, modifiers, isExtended);

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (_viewModel is null)
            return;

        var (keyCode, scanCode, isExtended) = GetKeyInfo(e);
        var modifiers = GetKeyModifiers(e.KeyModifiers);
        _viewModel.SendKeyUp(keyCode, scanCode, modifiers, isExtended);

        e.Handled = true;
    }

    private (float X, float Y) GetNormalizedPosition(PointerEventArgs e)
    {
        var point = e.GetPosition(FrameImage);
        var bounds = FrameImage.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return (-1, -1);

        var x = (float)(point.X / bounds.Width);
        var y = (float)(point.Y / bounds.Height);

        return (x, y);
    }

    private static ProtocolMouseButton? GetMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
            return ProtocolMouseButton.Left;
        if (properties.IsRightButtonPressed)
            return ProtocolMouseButton.Right;
        if (properties.IsMiddleButtonPressed)
            return ProtocolMouseButton.Middle;
        return null;
    }

    private static ProtocolKeyModifiers GetKeyModifiers(KeyModifiers modifiers)
    {
        var result = ProtocolKeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= ProtocolKeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= ProtocolKeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= ProtocolKeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            result |= ProtocolKeyModifiers.Win;
        return result;
    }

    private static (ushort KeyCode, ushort ScanCode, bool IsExtended) GetKeyInfo(KeyEventArgs e)
    {
        // Map Avalonia Key to Windows Virtual Key code
        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);

        // Scan code - use key value as fallback, real scan codes require platform-specific code
        var scanCode = (ushort)e.Key;

        // Extended keys include: right Alt, right Ctrl, Insert, Delete, Home, End, Page Up, Page Down,
        // arrow keys, Num Lock, Break, Print Screen, numpad Enter, numpad /
        var isExtended = e.Key is
            Key.RightAlt or Key.RightCtrl or
            Key.Insert or Key.Delete or Key.Home or Key.End or
            Key.PageUp or Key.PageDown or
            Key.Up or Key.Down or Key.Left or Key.Right or
            Key.NumLock or Key.Pause or Key.PrintScreen;

        return (keyCode, scanCode, isExtended);
    }
}

/// <summary>
/// Helper to convert Avalonia Key to Windows Virtual Key codes.
/// </summary>
internal static class KeyInterop
{
    public static int VirtualKeyFromKey(Key key)
    {
        // Direct mapping for common keys - Avalonia Key enum values roughly map to VK codes
        return key switch
        {
            Key.None => 0,
            Key.Cancel => 0x03, // VK_CANCEL
            Key.Back => 0x08, // VK_BACK
            Key.Tab => 0x09, // VK_TAB
            Key.Clear => 0x0C, // VK_CLEAR
            Key.Return => 0x0D, // VK_RETURN
            Key.Pause => 0x13, // VK_PAUSE
            Key.CapsLock => 0x14, // VK_CAPITAL
            Key.Escape => 0x1B, // VK_ESCAPE
            Key.Space => 0x20, // VK_SPACE
            Key.PageUp => 0x21, // VK_PRIOR
            Key.PageDown => 0x22, // VK_NEXT
            Key.End => 0x23, // VK_END
            Key.Home => 0x24, // VK_HOME
            Key.Left => 0x25, // VK_LEFT
            Key.Up => 0x26, // VK_UP
            Key.Right => 0x27, // VK_RIGHT
            Key.Down => 0x28, // VK_DOWN
            Key.Select => 0x29, // VK_SELECT
            Key.Print => 0x2A, // VK_PRINT
            Key.Execute => 0x2B, // VK_EXECUTE
            Key.PrintScreen => 0x2C, // VK_SNAPSHOT
            Key.Insert => 0x2D, // VK_INSERT
            Key.Delete => 0x2E, // VK_DELETE
            Key.Help => 0x2F, // VK_HELP
            Key.D0 => 0x30,
            Key.D1 => 0x31,
            Key.D2 => 0x32,
            Key.D3 => 0x33,
            Key.D4 => 0x34,
            Key.D5 => 0x35,
            Key.D6 => 0x36,
            Key.D7 => 0x37,
            Key.D8 => 0x38,
            Key.D9 => 0x39,
            Key.A => 0x41,
            Key.B => 0x42,
            Key.C => 0x43,
            Key.D => 0x44,
            Key.E => 0x45,
            Key.F => 0x46,
            Key.G => 0x47,
            Key.H => 0x48,
            Key.I => 0x49,
            Key.J => 0x4A,
            Key.K => 0x4B,
            Key.L => 0x4C,
            Key.M => 0x4D,
            Key.N => 0x4E,
            Key.O => 0x4F,
            Key.P => 0x50,
            Key.Q => 0x51,
            Key.R => 0x52,
            Key.S => 0x53,
            Key.T => 0x54,
            Key.U => 0x55,
            Key.V => 0x56,
            Key.W => 0x57,
            Key.X => 0x58,
            Key.Y => 0x59,
            Key.Z => 0x5A,
            Key.LWin => 0x5B, // VK_LWIN
            Key.RWin => 0x5C, // VK_RWIN
            Key.Apps => 0x5D, // VK_APPS
            Key.Sleep => 0x5F, // VK_SLEEP
            Key.NumPad0 => 0x60, // VK_NUMPAD0
            Key.NumPad1 => 0x61,
            Key.NumPad2 => 0x62,
            Key.NumPad3 => 0x63,
            Key.NumPad4 => 0x64,
            Key.NumPad5 => 0x65,
            Key.NumPad6 => 0x66,
            Key.NumPad7 => 0x67,
            Key.NumPad8 => 0x68,
            Key.NumPad9 => 0x69,
            Key.Multiply => 0x6A, // VK_MULTIPLY
            Key.Add => 0x6B, // VK_ADD
            Key.Separator => 0x6C, // VK_SEPARATOR
            Key.Subtract => 0x6D, // VK_SUBTRACT
            Key.Decimal => 0x6E, // VK_DECIMAL
            Key.Divide => 0x6F, // VK_DIVIDE
            Key.F1 => 0x70,
            Key.F2 => 0x71,
            Key.F3 => 0x72,
            Key.F4 => 0x73,
            Key.F5 => 0x74,
            Key.F6 => 0x75,
            Key.F7 => 0x76,
            Key.F8 => 0x77,
            Key.F9 => 0x78,
            Key.F10 => 0x79,
            Key.F11 => 0x7A,
            Key.F12 => 0x7B,
            Key.F13 => 0x7C,
            Key.F14 => 0x7D,
            Key.F15 => 0x7E,
            Key.F16 => 0x7F,
            Key.F17 => 0x80,
            Key.F18 => 0x81,
            Key.F19 => 0x82,
            Key.F20 => 0x83,
            Key.F21 => 0x84,
            Key.F22 => 0x85,
            Key.F23 => 0x86,
            Key.F24 => 0x87,
            Key.NumLock => 0x90, // VK_NUMLOCK
            Key.Scroll => 0x91, // VK_SCROLL
            Key.LeftShift => 0xA0, // VK_LSHIFT
            Key.RightShift => 0xA1, // VK_RSHIFT
            Key.LeftCtrl => 0xA2, // VK_LCONTROL
            Key.RightCtrl => 0xA3, // VK_RCONTROL
            Key.LeftAlt => 0xA4, // VK_LMENU
            Key.RightAlt => 0xA5, // VK_RMENU
            Key.OemSemicolon => 0xBA, // VK_OEM_1 (;:)
            Key.OemPlus => 0xBB, // VK_OEM_PLUS
            Key.OemComma => 0xBC, // VK_OEM_COMMA
            Key.OemMinus => 0xBD, // VK_OEM_MINUS
            Key.OemPeriod => 0xBE, // VK_OEM_PERIOD
            Key.OemQuestion => 0xBF, // VK_OEM_2 (/?)
            Key.OemTilde => 0xC0, // VK_OEM_3 (`~)
            Key.OemOpenBrackets => 0xDB, // VK_OEM_4 ([{)
            Key.OemPipe => 0xDC, // VK_OEM_5 (\|)
            Key.OemCloseBrackets => 0xDD, // VK_OEM_6 (]})
            Key.OemQuotes => 0xDE, // VK_OEM_7 ('")
            Key.OemBackslash => 0xE2, // VK_OEM_102
            _ => (int)key // Fallback
        };
    }
}

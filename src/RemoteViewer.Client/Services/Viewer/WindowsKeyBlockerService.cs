#if WINDOWS
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RemoteViewer.Client.Services.Viewer;

public sealed class WindowsKeyBlockerService : IWindowsKeyBlockerService, IDisposable
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_TAB = 0x09;
    private const int VK_F4 = 0x73;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SNAPSHOT = 0x2C;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;

    private readonly ILogger<WindowsKeyBlockerService> _logger;
    private readonly List<Func<bool>> _suppressCallbacks = [];
    private readonly Lock _lock = new();

    private bool _leftAltPressed;
    private bool _rightAltPressed;
    private bool _leftCtrlPressed;
    private bool _rightCtrlPressed;
    private bool _leftShiftPressed;
    private bool _rightShiftPressed;

    public event Action<InterceptedShortcut>? ShortcutIntercepted;

    private HHOOK _keyboardHook;
    private HOOKPROC? _keyboardProc;
    private bool _disposed;

    public WindowsKeyBlockerService(ILogger<WindowsKeyBlockerService> logger)
    {
        this._logger = logger;
    }

    public IDisposable StartBlocking(Func<bool> shouldSuppressShortcuts)
    {
        lock (this._lock)
        {
            this._suppressCallbacks.Add(shouldSuppressShortcuts);

            if (this._suppressCallbacks.Count == 1)
            {
                this._keyboardProc = this.KeyboardHookCallback;

                this._keyboardHook = PInvoke.SetWindowsHookEx(
                    WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
                    this._keyboardProc,
                    HINSTANCE.Null,
                    0);

                if (this._keyboardHook.IsNull)
                {
                    this._logger.LogError("Failed to install shortcut blocker hook: {ErrorCode}", Marshal.GetLastWin32Error());
                }
                else
                {
                    this._logger.LogDebug("Shortcut blocker started");
                }
            }

            return new BlockingHandle(this, shouldSuppressShortcuts);
        }
    }

    private void StopBlocking(Func<bool> shouldSuppressShortcuts)
    {
        lock (this._lock)
        {
            this._suppressCallbacks.Remove(shouldSuppressShortcuts);

            if (this._suppressCallbacks.Count > 0)
                return;

            if (!this._keyboardHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(this._keyboardHook);
                this._keyboardHook = default;
            }

            this._keyboardProc = null;

            this._logger.LogDebug("Shortcut blocker stopped");
        }
    }

    private sealed class BlockingHandle(WindowsKeyBlockerService service, Func<bool> callback) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (this._disposed)
                return;

            this._disposed = true;
            service.StopBlocking(callback);
        }
    }

    private LRESULT KeyboardHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            unsafe
            {
                var hookStruct = (KBDLLHOOKSTRUCT*)lParam.Value;
                var vkCode = (ushort)hookStruct->vkCode;
                var msg = (uint)wParam.Value;
                var isKeyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

                // Always track modifier state (even when not suppressing)
                this.UpdateModifierState(vkCode, isKeyDown);

                // Check if we should intercept shortcuts
                if (this.ShouldSuppressShortcuts() && this.ShouldInterceptKey(vkCode, out var shortcutName))
                {
                    var shortcut = new InterceptedShortcut(
                        vkCode,
                        isKeyDown,
                        this.AltPressed,
                        this.CtrlPressed,
                        this.ShiftPressed);

                    this.ShortcutIntercepted?.Invoke(shortcut);

                    this._logger.LogTrace("Blocked {ShortcutName}: VK={VkCode}", shortcutName, vkCode);
                    return new LRESULT(1);
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private void UpdateModifierState(ushort vkCode, bool isKeyDown)
    {
        switch (vkCode)
        {
            case VK_LMENU:
                this._leftAltPressed = isKeyDown;
                break;
            case VK_RMENU:
                this._rightAltPressed = isKeyDown;
                break;
            case VK_LCONTROL:
                this._leftCtrlPressed = isKeyDown;
                break;
            case VK_RCONTROL:
                this._rightCtrlPressed = isKeyDown;
                break;
            case VK_LSHIFT:
                this._leftShiftPressed = isKeyDown;
                break;
            case VK_RSHIFT:
                this._rightShiftPressed = isKeyDown;
                break;
        }
    }

    private bool AltPressed => this._leftAltPressed || this._rightAltPressed;
    private bool CtrlPressed => this._leftCtrlPressed || this._rightCtrlPressed;
    private bool ShiftPressed => this._leftShiftPressed || this._rightShiftPressed;

    private bool ShouldInterceptKey(ushort vkCode, out string shortcutName)
    {
        // Windows key
        if (vkCode is VK_LWIN or VK_RWIN)
        {
            shortcutName = "Windows key";
            return true;
        }

        // Alt+Tab
        if (vkCode == VK_TAB && this.AltPressed)
        {
            shortcutName = "Alt+Tab";
            return true;
        }

        // Alt+F4
        if (vkCode == VK_F4 && this.AltPressed)
        {
            shortcutName = "Alt+F4";
            return true;
        }

        // Ctrl+Shift+Esc
        if (vkCode == VK_ESCAPE && this.CtrlPressed && this.ShiftPressed)
        {
            shortcutName = "Ctrl+Shift+Esc";
            return true;
        }

        // Print Screen (with or without Alt)
        if (vkCode == VK_SNAPSHOT)
        {
            shortcutName = this.AltPressed ? "Alt+PrintScreen" : "PrintScreen";
            return true;
        }

        shortcutName = "";
        return false;
    }

    private bool ShouldSuppressShortcuts()
    {
        lock (this._lock)
        {
            foreach (var callback in this._suppressCallbacks)
            {
                if (callback())
                    return true;
            }
            return false;
        }
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        lock (this._lock)
        {
            this._suppressCallbacks.Clear();

            if (!this._keyboardHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(this._keyboardHook);
                this._keyboardHook = default;
            }

            this._keyboardProc = null;
        }
    }
}
#endif

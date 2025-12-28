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
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104;
    private const uint WM_SYSKEYUP = 0x0105;

    private readonly ILogger<WindowsKeyBlockerService> _logger;
    private readonly List<Func<bool>> _suppressCallbacks = [];
    private readonly Lock _lock = new();

    public event Action<ushort>? WindowsKeyDown;
    public event Action<ushort>? WindowsKeyUp;

    private HHOOK _keyboardHook;
    private HOOKPROC? _keyboardProc;
    private bool _disposed;

    public WindowsKeyBlockerService(ILogger<WindowsKeyBlockerService> logger)
    {
        this._logger = logger;
    }

    public IDisposable StartBlocking(Func<bool> shouldSuppressWindowsKey)
    {
        lock (this._lock)
        {
            this._suppressCallbacks.Add(shouldSuppressWindowsKey);

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
                    this._logger.LogError("Failed to install Windows key blocker hook: {ErrorCode}", Marshal.GetLastWin32Error());
                }
                else
                {
                    this._logger.LogDebug("Windows key blocker started");
                }
            }

            return new BlockingHandle(this, shouldSuppressWindowsKey);
        }
    }

    private void StopBlocking(Func<bool> shouldSuppressWindowsKey)
    {
        lock (this._lock)
        {
            this._suppressCallbacks.Remove(shouldSuppressWindowsKey);

            if (this._suppressCallbacks.Count > 0)
                return;

            if (!this._keyboardHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(this._keyboardHook);
                this._keyboardHook = default;
            }

            this._keyboardProc = null;

            this._logger.LogDebug("Windows key blocker stopped");
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
        if (nCode >= 0 && this.ShouldSuppressWindowsKey())
        {
            unsafe
            {
                var hookStruct = (KBDLLHOOKSTRUCT*)lParam.Value;
                var vkCode = (ushort)hookStruct->vkCode;

                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    // Raise event before blocking
                    var msg = (uint)wParam.Value;
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                        this.WindowsKeyDown?.Invoke(vkCode);
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                        this.WindowsKeyUp?.Invoke(vkCode);

                    this._logger.LogTrace("Blocked Windows key: {VkCode}", vkCode);
                    return new LRESULT(1);
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private bool ShouldSuppressWindowsKey()
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

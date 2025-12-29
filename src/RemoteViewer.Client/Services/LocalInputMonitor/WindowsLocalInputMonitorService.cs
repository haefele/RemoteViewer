#if WINDOWS
using System.Runtime.InteropServices;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace RemoteViewer.Client.Services.LocalInputMonitor;

public sealed class WindowsLocalInputMonitorService : ILocalInputMonitorService, IDisposable
{
    private const uint LLKHF_INJECTED = 0x10;
    private const uint LLMHF_INJECTED = 0x01;

    private static readonly long s_suppressionTicks = TimeSpan.FromMilliseconds(500).Ticks;

    private readonly Dispatcher _dispatcher;
    private readonly ILogger<WindowsLocalInputMonitorService> _logger;

    private HHOOK _keyboardHook;
    private HHOOK _mouseHook;
    private HOOKPROC? _keyboardProc;  // Must keep reference to prevent GC
    private HOOKPROC? _mouseProc;     // Must keep reference to prevent GC
    private long _lastLocalInputTicks;
    private int _referenceCount;
    private bool _disposed;

    public WindowsLocalInputMonitorService(Dispatcher dispatcher, ILogger<WindowsLocalInputMonitorService> logger)
    {
        this._dispatcher = dispatcher;
        this._logger = logger;
    }

    public bool ShouldSuppressViewerInput()
    {
        var lastTicks = Volatile.Read(ref this._lastLocalInputTicks);
        var elapsedTicks = DateTime.UtcNow.Ticks - lastTicks;
        return elapsedTicks < s_suppressionTicks;
    }

    public void StartMonitoring()
    {
        // Only install hooks on first caller
        if (Interlocked.Increment(ref this._referenceCount) > 1)
            return;

        this._dispatcher.Post(StartCore);

        void StartCore()
        {
            // Keep references to delegates to prevent GC collection
            this._keyboardProc = this.KeyboardHookCallback;
            this._mouseProc = this.MouseHookCallback;

            // Install keyboard hook (WH_KEYBOARD_LL = 13)
            this._keyboardHook = PInvoke.SetWindowsHookEx(
                WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
                this._keyboardProc,
                HINSTANCE.Null,  // For global low-level hooks, module must be null
                0);              // 0 = hook all threads

            if (this._keyboardHook.IsNull)
            {
                this._logger.KeyboardHookFailed(Marshal.GetLastWin32Error());
            }

            // Install mouse hook (WH_MOUSE_LL = 14)
            this._mouseHook = PInvoke.SetWindowsHookEx(
                WINDOWS_HOOK_ID.WH_MOUSE_LL,
                this._mouseProc,
                HINSTANCE.Null,
                0);

            if (this._mouseHook.IsNull)
            {
                this._logger.MouseHookFailed(Marshal.GetLastWin32Error());
            }

            this._logger.MonitoringStarted();
        }
    }

    public void StopMonitoring()
    {
        // Only unhook when last caller stops
        if (Interlocked.Decrement(ref this._referenceCount) > 0)
            return;

        this._dispatcher.Post(StopCore);

        void StopCore()
        {
            if (!this._keyboardHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(this._keyboardHook);
                this._keyboardHook = default;
            }

            if (!this._mouseHook.IsNull)
            {
                PInvoke.UnhookWindowsHookEx(this._mouseHook);
                this._mouseHook = default;
            }

            this._keyboardProc = null;
            this._mouseProc = null;

            this._logger.MonitoringStopped();
        }
    }

    private LRESULT KeyboardHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            unsafe
            {
                var hookStruct = (KBDLLHOOKSTRUCT*)lParam.Value;

                // Check if this is NOT injected input (i.e., local hardware input)
                if (((uint)hookStruct->flags & LLKHF_INJECTED) == 0)
                {
                    this.RecordLocalInput();
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private LRESULT MouseHookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            unsafe
            {
                var hookStruct = (MSLLHOOKSTRUCT*)lParam.Value;

                // Check if this is NOT injected input (i.e., local hardware input)
                if ((hookStruct->flags & LLMHF_INJECTED) == 0)
                {
                    this.RecordLocalInput();
                }
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, nCode, wParam, lParam);
    }

    private void RecordLocalInput()
    {
        Volatile.Write(ref this._lastLocalInputTicks, DateTime.UtcNow.Ticks);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        // Force stop regardless of reference count
        this._referenceCount = 0;

        if (!this._keyboardHook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(this._keyboardHook);
            this._keyboardHook = default;
        }

        if (!this._mouseHook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(this._mouseHook);
            this._mouseHook = default;
        }

        this._keyboardProc = null;
        this._mouseProc = null;
    }
}
#endif

namespace RemoteViewer.Client.Services.Viewer;

public readonly record struct InterceptedShortcut(
    ushort VirtualKeyCode,
    bool IsKeyDown,
    bool Alt,
    bool Ctrl,
    bool Shift
);

public interface IWindowsKeyBlockerService
{
    IDisposable StartBlocking(Func<bool> shouldSuppressShortcuts);

    event Action<InterceptedShortcut>? ShortcutIntercepted;
}

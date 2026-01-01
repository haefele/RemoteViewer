namespace RemoteViewer.Client.Services.Viewer;

public sealed class NullWindowsKeyBlockerService : IWindowsKeyBlockerService
{
#pragma warning disable CS0067 // Events are never used (this is a null implementation)
    public event Action<InterceptedShortcut>? ShortcutIntercepted;
#pragma warning restore CS0067

    public IDisposable StartBlocking(Func<bool> shouldSuppressShortcuts) => NullDisposable.Instance;

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }
}

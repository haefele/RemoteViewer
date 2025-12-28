namespace RemoteViewer.Client.Services.Viewer;

public interface IWindowsKeyBlockerService
{
    IDisposable StartBlocking(Func<bool> shouldSuppressWindowsKey);

    event Action<ushort>? WindowsKeyDown;
    event Action<ushort>? WindowsKeyUp;
}

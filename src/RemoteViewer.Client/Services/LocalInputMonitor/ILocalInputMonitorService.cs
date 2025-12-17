namespace RemoteViewer.Client.Services.LocalInputMonitor;

public interface ILocalInputMonitorService
{
    bool ShouldSuppressViewerInput();
    void StartMonitoring();
    void StopMonitoring();
}

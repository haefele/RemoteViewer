namespace RemoteViewer.Client.Services.LocalInputMonitor;

public class NullLocalInputMonitorService : ILocalInputMonitorService
{
    public bool ShouldSuppressViewerInput() => false;

    public void StartMonitoring() { }

    public void StopMonitoring() { }
}

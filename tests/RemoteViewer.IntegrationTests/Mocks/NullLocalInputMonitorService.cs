using RemoteViewer.Client.Services.LocalInputMonitor;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullLocalInputMonitorService : ILocalInputMonitorService
{
    public bool ShouldSuppressViewerInput() => false;
    public void StartMonitoring() { }
    public void StopMonitoring() { }
}

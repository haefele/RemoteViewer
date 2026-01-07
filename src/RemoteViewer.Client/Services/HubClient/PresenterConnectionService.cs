using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WinServiceIpc;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class PresenterConnectionService : IPresenterServiceImpl, IDisposable
{
    private readonly Connection _connection;
    private readonly IDisplayService _displayService;
    private readonly IScreenshotService _screenshotService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
    private readonly WinServiceRpcClient _winServiceRpcClient;
    private readonly ILogger<PresenterConnectionService> _logger;

    private readonly Lock _selectionsLock = new();
    private readonly Dictionary<string, string?> _viewerDisplaySelections = new(StringComparer.Ordinal);

    private readonly Timer _propertiesSyncTimer;

    private bool _disposed;

    public PresenterConnectionService(
        Connection connection,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        WinServiceRpcClient winServiceRpcClient,
        ILogger<PresenterConnectionService> logger)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._screenshotService = screenshotService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._winServiceRpcClient = winServiceRpcClient;
        this._logger = logger;

        this._localInputMonitor.StartMonitoring();

        // Sync connection properties periodically (every 3 seconds) to ensure eventual consistency
        // Delay the first callback to avoid racing with connection setup operations
        this._propertiesSyncTimer = new Timer(this.SyncPropertiesCallback, null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }

    private async void SyncPropertiesCallback(object? state)
    {
        if (this._disposed)
            return;

        try
        {
            var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, CancellationToken.None);
            var canSendSas = this._winServiceRpcClient.IsConnected;

            await this._connection.UpdateConnectionPropertiesAndSend(
                current => current with
                {
                    AvailableDisplays = displays.ToList(),
                    CanSendSecureAttentionSequence = canSendSas
                });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error syncing connection properties");
        }
    }

    string? IPresenterServiceImpl.GetViewerDisplayId(string viewerClientId)
    {
        using (this._selectionsLock.EnterScope())
        {
            return this._viewerDisplaySelections.TryGetValue(viewerClientId, out var displayId)
                ? displayId
                : null;
        }
    }

    async Task<string?> IPresenterServiceImpl.CycleViewerDisplayAsync(string viewerClientId, CancellationToken ct)
    {
        var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, ct);
        if (displays.Count == 0)
            return null;

        string? currentDisplayId;
        using (this._selectionsLock.EnterScope())
        {
            this._viewerDisplaySelections.TryGetValue(viewerClientId, out currentDisplayId);
        }

        var currentIndex = -1;
        for (var i = 0; i < displays.Count; i++)
        {
            if (string.Equals(displays[i].Id, currentDisplayId, StringComparison.Ordinal))
            {
                currentIndex = i;
                break;
            }
        }

        if (currentIndex < 0)
        {
            currentIndex = displays
                .Select((d, i) => (Display: d, Index: i))
                .FirstOrDefault(x => x.Display.IsPrimary)
                .Index;
        }

        var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % displays.Count;
        var nextDisplayId = displays[nextIndex].Id;

        return await ((IPresenterServiceImpl)this).SelectViewerDisplayAsync(viewerClientId, nextDisplayId, ct);
    }

    async Task<string?> IPresenterServiceImpl.SelectViewerDisplayAsync(string viewerClientId, string displayId, CancellationToken ct)
    {
        var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, ct);

        // Validate the display exists
        var display = displays.FirstOrDefault(d => string.Equals(d.Id, displayId, StringComparison.Ordinal));
        if (display is null)
            return null;

        using (this._selectionsLock.EnterScope())
        {
            this._viewerDisplaySelections[viewerClientId] = displayId;
        }

        // Force immediate keyframe so viewer doesn't see black screen
        await this._screenshotService.ForceKeyframe(displayId, ct);

        this._logger.LogDebug("Viewer {ViewerId} selected display {DisplayId}", viewerClientId, displayId);
        return displayId;
    }

    async Task<List<string>> IPresenterServiceImpl.GetViewerIdsWatchingDisplayAsync(string displayId, CancellationToken ct)
    {
        var result = new List<string>();

        var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, ct);
        var primaryDisplayId = displays.FirstOrDefault(d => d.IsPrimary)?.Id;
        var viewers = this._connection.Viewers;

        using (this._selectionsLock.EnterScope())
        {
            foreach (var viewer in viewers)
            {
                this._viewerDisplaySelections.TryGetValue(viewer.ClientId, out var selectedDisplayId);
                var effectiveDisplayId = selectedDisplayId ?? primaryDisplayId;

                if (string.Equals(effectiveDisplayId, displayId, StringComparison.Ordinal))
                {
                    result.Add(viewer.ClientId);
                }
            }
        }

        return result;
    }

    async Task<HashSet<string>> IPresenterServiceImpl.GetDisplaysWithViewers(CancellationToken ct)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, ct);
        var primaryDisplayId = displays.FirstOrDefault(d => d.IsPrimary)?.Id;
        var viewers = this._connection.Viewers;

        using (this._selectionsLock.EnterScope())
        {
            foreach (var viewer in viewers)
            {
                this._viewerDisplaySelections.TryGetValue(viewer.ClientId, out var selectedDisplayId);
                var effectiveDisplayId = selectedDisplayId ?? primaryDisplayId;

                if (effectiveDisplayId is not null)
                {
                    result.Add(effectiveDisplayId);
                }
            }
        }

        return result;
    }

    async void IPresenterServiceImpl.HandleMouseMove(string senderClientId, float x, float y)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(senderClientId))
                return;

            var displayId = ((IPresenterServiceImpl)this).GetViewerDisplayId(senderClientId);
            var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Id == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseMove(display, x, y, this._connection.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling mouse move from {SenderClientId}", senderClientId);
        }
    }

    async void IPresenterServiceImpl.HandleMouseButton(string senderClientId, float x, float y, MouseButton button, bool isDown)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(senderClientId))
                return;

            var displayId = ((IPresenterServiceImpl)this).GetViewerDisplayId(senderClientId);
            var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Id == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseButton(display, button, isDown, x, y, this._connection.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling mouse button from {SenderClientId}", senderClientId);
        }
    }

    async void IPresenterServiceImpl.HandleMouseWheel(string senderClientId, float x, float y, float deltaX, float deltaY)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(senderClientId))
                return;

            var displayId = ((IPresenterServiceImpl)this).GetViewerDisplayId(senderClientId);
            var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Id == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseWheel(display, deltaX, deltaY, x, y, this._connection.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling mouse wheel from {SenderClientId}", senderClientId);
        }
    }

    async void IPresenterServiceImpl.HandleKey(string senderClientId, ushort keyCode, KeyModifiers modifiers, bool isDown)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(senderClientId))
                return;

            await this._inputInjectionService.InjectKey(keyCode, isDown, this._connection.ConnectionId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling key from {SenderClientId}", senderClientId);
        }
    }

    async void IPresenterServiceImpl.HandleSecureAttentionSequence(string senderClientId)
    {
        try
        {
            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(senderClientId))
            {
                this._logger.LogInformation("Ignoring Ctrl+Alt+Del from blocked viewer: {ViewerId}", senderClientId);
                return;
            }

            if (!this._winServiceRpcClient.IsConnected || this._winServiceRpcClient.Proxy is null)
            {
                this._logger.LogWarning("Cannot send Ctrl+Alt+Del - not connected to Windows Service");
                return;
            }

            var sessionId = (uint)System.Diagnostics.Process.GetCurrentProcess().SessionId;
            var result = await this._winServiceRpcClient.Proxy.SendSecureAttentionSequence(
                this._connection.ConnectionId,
                sessionId,
                CancellationToken.None);

            if (result)
            {
                this._logger.LogInformation("Sent Ctrl+Alt+Del on behalf of viewer: {ViewerId}", senderClientId);
            }
            else
            {
                this._logger.LogWarning("SendSAS failed for viewer: {ViewerId}", senderClientId);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling Ctrl+Alt+Del request from {SenderClientId}", senderClientId);
        }
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._propertiesSyncTimer.Dispose();

        this._localInputMonitor.StopMonitoring();

        this._inputInjectionService.ReleaseAllModifiers(this._connection.ConnectionId, CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

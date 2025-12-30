using System.Linq;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsIpc;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class PresenterConnectionService : IPresenterServiceImpl, IDisposable
{
    private readonly Connection _connection;
    private readonly IDisplayService _displayService;
    private readonly IScreenshotService _screenshotService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
    private readonly SessionRecorderRpcClient _rpcClient;
    private readonly ILogger<PresenterConnectionService> _logger;

    private readonly Lock _selectionsLock = new();
    private readonly Dictionary<string, string?> _viewerDisplaySelections = new(StringComparer.Ordinal);

    private bool _disposed;

    public PresenterConnectionService(
        Connection connection,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        SessionRecorderRpcClient rpcClient,
        ILogger<PresenterConnectionService> logger)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._screenshotService = screenshotService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._rpcClient = rpcClient;
        this._logger = logger;

        this._connection.Closed += this.OnConnectionClosed;
        this._rpcClient.ConnectionStatusChanged += this.RpcClient_ConnectionStatusChanged;

        this._localInputMonitor.StartMonitoring();
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
        var displays = await this._displayService.GetDisplays(ct);
        if (displays.Count == 0)
            return null;

        string newDisplayId;
        using (this._selectionsLock.EnterScope())
        {
            this._viewerDisplaySelections.TryGetValue(viewerClientId, out var currentDisplayId);

            var currentIndex = -1;
            for (var i = 0; i < displays.Count; i++)
            {
                if (string.Equals(displays[i].Name, currentDisplayId, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                var primaryIndex = displays
                    .Select((d, i) => (Display: d, Index: i))
                    .FirstOrDefault(x => x.Display.IsPrimary)
                    .Index;
                currentIndex = primaryIndex;
            }

            var nextIndex = currentIndex < 0 ? 0 : (currentIndex + 1) % displays.Count;
            newDisplayId = displays[nextIndex].Name;
            this._viewerDisplaySelections[viewerClientId] = newDisplayId;
        }

        // Force immediate keyframe so viewer doesn't see black screen
        await this._screenshotService.ForceKeyframe(newDisplayId, ct);

        this._logger.LogDebug("Viewer {ViewerId} switched to display {DisplayId}", viewerClientId, newDisplayId);
        return newDisplayId;
    }

    async Task<List<string>> IPresenterServiceImpl.GetViewerIdsWatchingDisplayAsync(string displayId, CancellationToken ct)
    {
        var result = new List<string>();

        var displays = await this._displayService.GetDisplays(ct);
        var primaryDisplayId = displays.FirstOrDefault(d => d.IsPrimary)?.Name;
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

        var displays = await this._displayService.GetDisplays(ct);
        var primaryDisplayId = displays.FirstOrDefault(d => d.IsPrimary)?.Name;
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
            var displays = await this._displayService.GetDisplays(CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Name == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseMove(display, x, y, CancellationToken.None);
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
            var displays = await this._displayService.GetDisplays(CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Name == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseButton(display, button, isDown, x, y, CancellationToken.None);
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
            var displays = await this._displayService.GetDisplays(CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Name == displayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            await this._inputInjectionService.InjectMouseWheel(display, deltaX, deltaY, x, y, CancellationToken.None);
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

            await this._inputInjectionService.InjectKey(keyCode, isDown, CancellationToken.None);
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

            if (!this._rpcClient.IsConnected || this._rpcClient.Proxy is null)
            {
                this._logger.LogWarning("Cannot send Ctrl+Alt+Del - not connected to Windows Service");
                return;
            }

            var result = await this._rpcClient.Proxy.SendSecureAttentionSequence(CancellationToken.None);
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

    private async void RpcClient_ConnectionStatusChanged(object? sender, EventArgs e)
    {
        try
        {
            await this._connection.UpdateConnectionPropertiesAndSend(current =>
            {
                return current with { CanSendSecureAttentionSequence = this._rpcClient.IsConnected };
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error updating connection properties after RPC status change");
        }
    }

    private void OnConnectionClosed(object? sender, EventArgs e)
    {
        try
        {
            this._inputInjectionService.ReleaseAllModifiers(CancellationToken.None).GetAwaiter().GetResult();

        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling connection closed event");
        }
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._connection.Closed -= this.OnConnectionClosed;
        this._rpcClient.ConnectionStatusChanged -= this.RpcClient_ConnectionStatusChanged;

        this._localInputMonitor.StopMonitoring();

        this._inputInjectionService.ReleaseAllModifiers(CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

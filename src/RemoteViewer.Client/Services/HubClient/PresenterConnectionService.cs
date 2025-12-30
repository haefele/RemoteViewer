using System.Linq;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsIpc;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class PresenterConnectionService : IDisposable
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

        this._connection.InputReceived += this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested += this.OnSecureAttentionSequenceRequested;
        this._connection.Closed += this.OnConnectionClosed;
        this._rpcClient.ConnectionStatusChanged += this.RpcClient_ConnectionStatusChanged;

        this._localInputMonitor.StartMonitoring();
    }

    internal string? GetViewerDisplayId(string viewerClientId)
    {
        using (this._selectionsLock.EnterScope())
        {
            return this._viewerDisplaySelections.TryGetValue(viewerClientId, out var displayId)
                ? displayId
                : null;
        }
    }

    internal async Task<string?> CycleViewerDisplayAsync(string viewerClientId, CancellationToken ct = default)
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

    internal async Task<List<string>> GetViewerIdsWatchingDisplayAsync(string displayId, CancellationToken ct = default)
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

    internal async Task<HashSet<string>> GetDisplaysWithViewers(CancellationToken ct = default)
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

    private async void RpcClient_ConnectionStatusChanged(object? sender, EventArgs e)
    {
        await this._connection.UpdateConnectionPropertiesAndSend(current =>
        {
            return current with { CanSendSecureAttentionSequence = this._rpcClient.IsConnected };
        });
    }

    private async void OnInputReceived(object? sender, InputReceivedEventArgs e)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(e.SenderClientId))
                return;

            var displays = await this._displayService.GetDisplays(CancellationToken.None);
            var display = displays.FirstOrDefault(d => d.Name == e.DisplayId) ?? displays.FirstOrDefault(d => d.IsPrimary);

            if (display is null)
                return;

            switch (e.Type)
            {
                case InputType.MouseMove:
                    if (e.X.HasValue && e.Y.HasValue)
                    {
                        await this._inputInjectionService.InjectMouseMove(display, e.X.Value, e.Y.Value, CancellationToken.None);
                    }
                    break;

                case InputType.MouseDown:
                    if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                    {
                        await this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: true, e.X.Value, e.Y.Value, CancellationToken.None);
                    }
                    break;

                case InputType.MouseUp:
                    if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                    {
                        await this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: false, e.X.Value, e.Y.Value, CancellationToken.None);
                    }
                    break;

                case InputType.MouseWheel:
                    if (e.X.HasValue && e.Y.HasValue && e.DeltaX.HasValue && e.DeltaY.HasValue)
                    {
                        await this._inputInjectionService.InjectMouseWheel(display, e.DeltaX.Value, e.DeltaY.Value, e.X.Value, e.Y.Value, CancellationToken.None);
                    }
                    break;

                case InputType.KeyDown:
                    if (e.KeyCode.HasValue)
                    {
                        await this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: true, CancellationToken.None);
                    }
                    break;

                case InputType.KeyUp:
                    if (e.KeyCode.HasValue)
                    {
                        await this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: false, CancellationToken.None);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling input from {SenderClientId}", e.SenderClientId);
        }
    }

    private async void OnSecureAttentionSequenceRequested(object? sender, SecureAttentionSequenceRequestedEventArgs e)
    {
        try
        {
            if (this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(e.SenderClientId))
            {
                this._logger.LogInformation("Ignoring Ctrl+Alt+Del from blocked viewer: {ViewerId}", e.SenderClientId);
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
                this._logger.LogInformation("Sent Ctrl+Alt+Del on behalf of viewer: {ViewerId}", e.SenderClientId);
            }
            else
            {
                this._logger.LogWarning("SendSAS failed for viewer: {ViewerId}", e.SenderClientId);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error handling Ctrl+Alt+Del request from {SenderClientId}", e.SenderClientId);
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

        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested -= this.OnSecureAttentionSequenceRequested;
        this._connection.Closed -= this.OnConnectionClosed;
        this._rpcClient.ConnectionStatusChanged -= this.RpcClient_ConnectionStatusChanged;

        this._localInputMonitor.StopMonitoring();

        this._inputInjectionService.ReleaseAllModifiers(CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

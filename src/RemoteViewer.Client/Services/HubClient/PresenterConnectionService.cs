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
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
    private readonly SessionRecorderRpcClient _rpcClient;
    private readonly ILogger<PresenterConnectionService> _logger;

    private readonly HashSet<string> _blockedViewerIds = new(StringComparer.Ordinal);
    private readonly object _blockedLock = new();

    private bool _disposed;

    public PresenterConnectionService(
        Connection connection,
        IDisplayService displayService,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        SessionRecorderRpcClient rpcClient,
        ILogger<PresenterConnectionService> logger)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._rpcClient = rpcClient;
        this._logger = logger;

        this._connection.InputReceived += this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested += this.OnSecureAttentionSequenceRequested;
        this._connection.Closed += this.OnConnectionClosed;

        this._localInputMonitor.StartMonitoring();
    }

    public void SetViewerInputBlocked(string viewerClientId, bool isBlocked)
    {
        lock (this._blockedLock)
        {
            if (isBlocked)
            {
                this._blockedViewerIds.Add(viewerClientId);
            }
            else
            {
                this._blockedViewerIds.Remove(viewerClientId);
            }
        }
    }

    private bool IsViewerBlocked(string viewerClientId)
    {
        lock (this._blockedLock)
        {
            return this._blockedViewerIds.Contains(viewerClientId);
        }
    }

    private async void OnInputReceived(object? sender, InputReceivedEventArgs e)
    {
        try
        {
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            if (this.IsViewerBlocked(e.SenderClientId))
                return;

            if (e.DisplayId is null)
                return;

            var display = await this.GetDisplayByIdAsync(e.DisplayId);
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
            if (this.IsViewerBlocked(e.SenderClientId))
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

    private async Task<Display?> GetDisplayByIdAsync(string displayId)
    {
        var displays = await this._displayService.GetDisplays(CancellationToken.None);
        return displays.FirstOrDefault(d => d.Name == displayId);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested -= this.OnSecureAttentionSequenceRequested;
        this._connection.Closed -= this.OnConnectionClosed;

        this._localInputMonitor.StopMonitoring();

        this._inputInjectionService.ReleaseAllModifiers(CancellationToken.None).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }
}

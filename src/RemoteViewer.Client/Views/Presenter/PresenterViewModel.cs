using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Services.WindowsIpc;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ConnectionHubClient _hubClient;
    private readonly IDisplayService _displayService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
    private readonly SessionRecorderRpcClient _rpcClient;
    private readonly ILogger<PresenterViewModel> _logger;

    public ToastsViewModel Toasts { get; }

    private DisplayCaptureManager? _captureManager;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "Presenting";

    [ObservableProperty]
    private string? _yourId;

    [ObservableProperty]
    private string? _yourPassword;

    public ObservableCollection<PresenterViewerDisplay> Viewers { get; } = [];
    public FileTransferService FileTransfers => this._connection.FileTransfers;

    public event EventHandler? CloseRequested;
    public event EventHandler<string>? CopyToClipboardRequested;
    public event EventHandler<IReadOnlyList<string>>? OpenFilePickerRequested;

    public PresenterViewModel(
        Connection connection,
        ConnectionHubClient hubClient,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        IFrameEncoder frameEncoder,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        SessionRecorderRpcClient rpcClient,
        IViewModelFactory viewModelFactory,
        ILogger<PresenterViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._hubClient = hubClient;
        this._displayService = displayService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._rpcClient = rpcClient;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this._connection.FileTransfers.Toasts = this.Toasts;

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.InputReceived += this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested += this.OnSecureAttentionSequenceRequested;
        this._connection.Closed += this.OnConnectionClosed;

        // Subscribe to credentials changes
        this._hubClient.CredentialsAssigned += this.OnCredentialsAssigned;

        // Start monitoring for local input to auto-suppress viewer input
        this._localInputMonitor.StartMonitoring();

        // Create and start capture manager
        this._captureManager = new DisplayCaptureManager(
            connection,
            displayService,
            screenshotService,
            frameEncoder,
            loggerFactory,
            loggerFactory.CreateLogger<DisplayCaptureManager>());
        this._captureManager.Start();
    }

    private void OnCredentialsAssigned(object? sender, CredentialsAssignedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.YourId = e.Username;
            this.YourPassword = e.Password;
        });
    }

    private void OnViewersChanged(object? sender, EventArgs e)
    {
        var viewers = this._connection.Viewers;

        // Log new/removed viewers for debugging
        this._logger.LogInformation("Viewer list changed: {ViewerCount} viewer(s)", viewers.Count);

        // Update UI
        Dispatcher.UIThread.Post(() =>
        {
            this.UpdateViewers(viewers);
        });
    }

    private void UpdateViewers(IReadOnlyList<ViewerInfo> viewers)
    {
        // Build set of current viewer IDs
        var currentViewerIds = viewers.Select(v => v.ClientId).ToHashSet();

        // Remove viewers that are no longer connected
        for (var i = this.Viewers.Count - 1; i >= 0; i--)
        {
            if (!currentViewerIds.Contains(this.Viewers[i].ClientId))
            {
                this.Viewers.RemoveAt(i);
            }
        }

        // Add new viewers
        var existingIds = this.Viewers.Select(p => p.ClientId).ToHashSet();
        foreach (var viewer in viewers)
        {
            if (!existingIds.Contains(viewer.ClientId))
            {
                this.Viewers.Add(new PresenterViewerDisplay(
                    viewer.ClientId,
                    viewer.DisplayName));
            }
        }
    }

    private async void OnInputReceived(object? sender, InputReceivedEventArgs e)
    {
        try
        {
            // Check for local presenter activity - suppress viewer input temporarily
            if (this._localInputMonitor.ShouldSuppressViewerInput())
                return;

            // Check if this specific viewer's input is blocked
            var viewer = this.Viewers.FirstOrDefault(v => v.ClientId == e.SenderClientId);
            if (viewer?.IsInputBlocked == true)
                return;

            // Get the display for this viewer's selection
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
            // Check if this viewer's input is blocked
            var viewer = this.Viewers.FirstOrDefault(v => v.ClientId == e.SenderClientId);
            if (viewer?.IsInputBlocked == true)
            {
                this._logger.LogInformation("Ignoring Ctrl+Alt+Del from blocked viewer: {ViewerId}", e.SenderClientId);
                return;
            }

            // Check if we have the RPC client connected to the Windows Service
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

    private async void OnConnectionClosed(object? sender, EventArgs e)
    {
        try
        {
            this._captureManager?.Dispose();
            this._captureManager = null;

            // Release any stuck modifier keys when connection closes
            await this._inputInjectionService.ReleaseAllModifiers(CancellationToken.None);

            Dispatcher.UIThread.Post(() =>
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            });
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

    [RelayCommand]
    private async Task StopPresentingAsync()
    {
        this._logger.LogInformation("User requested to stop presenting for connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }

    [RelayCommand]
    private void CopyCredentials()
    {
        if (this.YourId is null || this.YourPassword is null)
            return;

        var text = $"""
                    ID: {this.YourId}
                    Password: {this.YourPassword}
                    """;
        this.CopyToClipboardRequested?.Invoke(this, text);
        this.Toasts.Success("ID and password copied to clipboard.");
    }

    [RelayCommand]
    private async Task GenerateNewPasswordAsync()
    {
        await this._hubClient.GenerateNewPassword();
    }

    [RelayCommand]
    private async Task CancelTransfer(IFileTransfer transfer)
    {
        await transfer.CancelAsync();
    }

    #region File Transfer - Send to Viewers

    private IReadOnlyList<string>? _pendingViewerIds;

    [RelayCommand]
    private void SendFile(IReadOnlyList<string>? viewerIds = null)
    {
        // If no specific viewers provided and multiple viewers, the UI should handle selection first
        if (viewerIds is null or { Count: 0 })
        {
            if (this.Viewers.Count == 0)
            {
                this.Toasts.Info("No viewers connected.");
                return;
            }

            // If only one viewer, auto-select
            if (this.Viewers.Count == 1)
            {
                viewerIds = [this.Viewers[0].ClientId];
            }
            else
            {
                // UI should have provided viewer IDs for multi-viewer scenario
                this.Toasts.Info("Select viewers first.");
                return;
            }
        }

        this._pendingViewerIds = viewerIds;
        this.OpenFilePickerRequested?.Invoke(this, viewerIds);
    }

    public async Task SendFileFromPathAsync(string filePath)
    {
        var viewerIds = this._pendingViewerIds;
        this._pendingViewerIds = null;

        if (viewerIds is null or { Count: 0 })
        {
            // Fallback: send to all viewers
            viewerIds = this.Viewers.Select(v => v.ClientId).ToList();
        }

        await this.SendFileToViewersAsync(filePath, viewerIds);
    }

    public async Task SendFileToViewersAsync(string filePath, IReadOnlyList<string> viewerIds)
    {
        if (!File.Exists(filePath))
        {
            this.Toasts.Error($"File not found: {filePath}");
            return;
        }

        foreach (var viewerId in viewerIds)
        {
            try
            {
                var transfer = await this._connection.FileTransfers.SendFileToViewerAsync(filePath, viewerId);
                this.Toasts.AddTransfer(transfer, isUpload: true);
                var viewer = this.Viewers.FirstOrDefault(v => v.ClientId == viewerId);
                this._logger.LogInformation("Started file send to {ViewerName}: {FileName} ({FileSize} bytes)",
                    viewer?.DisplayName ?? viewerId, transfer.FileName, transfer.FileSize);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to initiate file send to {ViewerId} for {FilePath}", viewerId, filePath);
                this.Toasts.Error($"Failed to send file: {ex.Message}");
            }
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        // Stop monitoring local input
        this._localInputMonitor.StopMonitoring();

        await this._connection.FileTransfers.CancelAllAsync();

        this._captureManager?.Dispose();
        await this._connection.DisconnectAsync();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.SecureAttentionSequenceRequested -= this.OnSecureAttentionSequenceRequested;
        this._connection.Closed -= this.OnConnectionClosed;
        this._hubClient.CredentialsAssigned -= this.OnCredentialsAssigned;

        GC.SuppressFinalize(this);
    }
}

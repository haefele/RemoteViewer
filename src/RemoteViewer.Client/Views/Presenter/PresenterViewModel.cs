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

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ConnectionHubClient _hubClient;
    private readonly IDisplayService _displayService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
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
    public ObservableCollection<IncomingFileTransfer> ActiveTransfers { get; } = [];

    public event EventHandler? CloseRequested;
    public event EventHandler<string>? CopyToClipboardRequested;
    public event EventHandler<FileTransferConfirmationEventArgs>? FileTransferConfirmationRequested;

    public PresenterViewModel(
        Connection connection,
        ConnectionHubClient hubClient,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        ScreenEncoder screenEncoder,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        IViewModelFactory viewModelFactory,
        ILogger<PresenterViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._hubClient = hubClient;
        this._displayService = displayService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        this.Title = $"Presenting - {connection.ConnectionId[..8]}...";

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.InputReceived += this.OnInputReceived;
        this._connection.Closed += this.OnConnectionClosed;

        // Subscribe to file transfer request event
        this._connection.FileSendRequestReceived += this.OnFileSendRequestReceived;

        // Subscribe to credentials changes
        this._hubClient.CredentialsAssigned += this.OnCredentialsAssigned;

        // Start monitoring for local input to auto-suppress viewer input
        this._localInputMonitor.StartMonitoring();

        // Create and start capture manager
        this._captureManager = new DisplayCaptureManager(
            connection,
            displayService,
            screenshotService,
            screenEncoder,
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

    private void OnInputReceived(object? sender, InputReceivedEventArgs e)
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

        var display = this.GetDisplayById(e.DisplayId);
        if (display is null)
            return;

        switch (e.Type)
        {
            case InputType.MouseMove:
                if (e.X.HasValue && e.Y.HasValue)
                {
                    this._inputInjectionService.InjectMouseMove(display, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseDown:
                if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                {
                    this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: true, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseUp:
                if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                {
                    this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: false, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseWheel:
                if (e.X.HasValue && e.Y.HasValue && e.DeltaX.HasValue && e.DeltaY.HasValue)
                {
                    this._inputInjectionService.InjectMouseWheel(display, e.DeltaX.Value, e.DeltaY.Value, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.KeyDown:
                if (e.KeyCode.HasValue)
                {
                    this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: true);
                }
                break;

            case InputType.KeyUp:
                if (e.KeyCode.HasValue)
                {
                    this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: false);
                }
                break;
        }
    }

    private void OnConnectionClosed(object? sender, EventArgs e)
    {
        this._captureManager?.Dispose();
        this._captureManager = null;

        // Release any stuck modifier keys when connection closes
        this._inputInjectionService.ReleaseAllModifiers();

        Dispatcher.UIThread.Post(() =>
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private Display? GetDisplayById(string displayId)
    {
        return this._displayService.GetDisplays().FirstOrDefault(d => d.Name == displayId);
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

    #region File Transfer
    private void OnFileSendRequestReceived(object? sender, FileSendRequestReceivedEventArgs e)
    {
        this._logger.LogInformation("Received file send request: {FileName} ({FileSize} bytes) from {ClientId}",
            e.FileName, e.FileSize, e.SenderClientId);

        Dispatcher.UIThread.Post(() =>
        {
            // Raise event so the view can show a confirmation dialog
            var args = new FileTransferConfirmationEventArgs(
                e.SenderClientId,
                e.TransferId,
                e.FileName,
                e.FileSize);

            this.FileTransferConfirmationRequested?.Invoke(this, args);
        });
    }

    public async Task AcceptFileTransferAsync(string senderClientId, string transferId, string fileName, long fileSize)
    {
        var transfer = new IncomingFileTransfer(senderClientId, transferId, fileName, fileSize, this._connection);

        transfer.Completed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            this.ActiveTransfers.Remove(transfer);
            this.Toasts.Success($"File received: {transfer.FileName}");
            transfer.Dispose();
        });

        transfer.Failed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            this.ActiveTransfers.Remove(transfer);
            this.Toasts.Error($"Transfer failed: {transfer.ErrorMessage ?? "Unknown error"}");
            transfer.Dispose();
        });

        this.ActiveTransfers.Add(transfer);
        await transfer.AcceptAsync();

        this._logger.LogInformation("Accepted file transfer: {TransferId} -> {DestinationPath}", transferId, transfer.DestinationPath);
    }

    public async Task RejectFileTransferAsync(string senderClientId, string transferId)
    {
        await this._connection.SendFileSendResponseAsync(transferId, accepted: false, error: "Transfer rejected by user", senderClientId);
        this._logger.LogInformation("Rejected file transfer: {TransferId}", transferId);
    }

    [RelayCommand]
    private async Task CancelTransfer(IncomingFileTransfer transfer)
    {
        await transfer.CancelAsync();
        this.ActiveTransfers.Remove(transfer);
        transfer.Dispose();
    }
    #endregion

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        // Stop monitoring local input
        this._localInputMonitor.StopMonitoring();

        // Cancel and dispose all active transfers
        foreach (var transfer in this.ActiveTransfers.ToList())
        {
            await transfer.CancelAsync();
            transfer.Dispose();
        }
        this.ActiveTransfers.Clear();

        this._captureManager?.Dispose();
        await this._connection.DisconnectAsync();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.Closed -= this.OnConnectionClosed;
        this._hubClient.CredentialsAssigned -= this.OnCredentialsAssigned;
        this._connection.FileSendRequestReceived -= this.OnFileSendRequestReceived;

        GC.SuppressFinalize(this);
    }
}

public sealed class FileTransferConfirmationEventArgs : EventArgs
{
    public FileTransferConfirmationEventArgs(string senderClientId, string transferId, string fileName, long fileSize)
    {
        this.SenderClientId = senderClientId;
        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;
    }

    public string SenderClientId { get; }
    public string TransferId { get; }
    public string FileName { get; }
    public long FileSize { get; }
}

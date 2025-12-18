using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.FileSystem;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ConnectionHubClient _hubClient;
    private readonly IDisplayService _displayService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILocalInputMonitorService _localInputMonitor;
    private readonly IFileSystemService _fileSystemService;
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
    public event EventHandler<IncomingFileRequestedEventArgs>? FileTransferConfirmationRequested;
    public event EventHandler<DownloadRequestedEventArgs>? FileDownloadConfirmationRequested;

    public PresenterViewModel(
        Connection connection,
        ConnectionHubClient hubClient,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        ScreenEncoder screenEncoder,
        IInputInjectionService inputInjectionService,
        ILocalInputMonitorService localInputMonitor,
        IFileSystemService fileSystemService,
        IViewModelFactory viewModelFactory,
        ILogger<PresenterViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._hubClient = hubClient;
        this._displayService = displayService;
        this._inputInjectionService = inputInjectionService;
        this._localInputMonitor = localInputMonitor;
        this._fileSystemService = fileSystemService;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        this.Title = $"Presenting - {connection.ConnectionId[..8]}...";

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.InputReceived += this.OnInputReceived;
        this._connection.Closed += this.OnConnectionClosed;

        // Subscribe to file transfer service events
        this._connection.FileTransfers.IncomingFileRequested += this.OnIncomingFileRequested;
        this._connection.FileTransfers.DownloadRequested += this.OnDownloadRequested;

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
    private void OnIncomingFileRequested(object? sender, IncomingFileRequestedEventArgs e)
    {
        this._logger.LogInformation("Received file upload request: {FileName} ({FileSize} bytes) from {ClientId}",
            e.FileName, e.FileSize, e.SenderClientId);

        this.FileTransferConfirmationRequested?.Invoke(this, e);
    }

    private void OnDownloadRequested(object? sender, DownloadRequestedEventArgs e)
    {
        this._logger.LogInformation("Received file download request: {FilePath} from {ClientId}",
            e.FilePath, e.RequesterClientId);

        this.FileDownloadConfirmationRequested?.Invoke(this, e);
    }

    public async Task AcceptFileTransferAsync(string senderClientId, string transferId, string fileName, long fileSize)
    {
        var transfer = await this._connection.FileTransfers.AcceptIncomingFileAsync(
            senderClientId, transferId, fileName, fileSize);

        this.Toasts.AddTransfer(transfer, isUpload: false);
        this._logger.LogInformation("Accepted file upload: {TransferId} -> {DestinationPath}", transferId, transfer.DestinationPath);
    }

    public async Task RejectFileTransferAsync(string senderClientId, string transferId)
    {
        await this._connection.FileTransfers.RejectIncomingFileAsync(senderClientId, transferId);
        this._logger.LogInformation("Rejected file upload: {TransferId}", transferId);
    }

    public async Task AcceptFileDownloadAsync(string requesterClientId, string transferId, string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                await this._connection.FileTransfers.RejectDownloadRequestAsync(requesterClientId, transferId, "File not found");
                return;
            }

            if (!this._fileSystemService.IsPathAllowed(filePath))
            {
                await this._connection.FileTransfers.RejectDownloadRequestAsync(requesterClientId, transferId, "Access denied");
                return;
            }

            var transfer = await this._connection.FileTransfers.AcceptDownloadRequestAsync(
                requesterClientId, transferId, filePath);

            this.Toasts.AddTransfer(transfer, isUpload: true);
            this._logger.LogInformation("Started serving download: {FilePath} -> {ClientId}", filePath, requesterClientId);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to accept download request for {FilePath}", filePath);
            await this._connection.FileTransfers.RejectDownloadRequestAsync(requesterClientId, transferId, ex.Message);
        }
    }

    public async Task RejectFileDownloadAsync(string requesterClientId, string transferId)
    {
        await this._connection.FileTransfers.RejectDownloadRequestAsync(requesterClientId, transferId);
        this._logger.LogInformation("Rejected file download: {TransferId}", transferId);
    }

    [RelayCommand]
    private async Task CancelTransfer(IFileTransfer transfer)
    {
        await transfer.CancelAsync();
    }
    #endregion

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        // Stop monitoring local input
        this._localInputMonitor.StopMonitoring();

        // Unsubscribe from file transfer service events
        this._connection.FileTransfers.IncomingFileRequested -= this.OnIncomingFileRequested;
        this._connection.FileTransfers.DownloadRequested -= this.OnDownloadRequested;

        await this._connection.FileTransfers.CancelAllAsync();

        this._captureManager?.Dispose();
        await this._connection.DisconnectAsync();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.Closed -= this.OnConnectionClosed;
        this._hubClient.CredentialsAssigned -= this.OnCredentialsAssigned;

        GC.SuppressFinalize(this);
    }
}

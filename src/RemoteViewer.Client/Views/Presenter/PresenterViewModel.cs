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
    public ObservableCollection<IncomingFileTransfer> ActiveUploads { get; } = [];
    public ObservableCollection<OutgoingFileTransfer> ActiveDownloads { get; } = [];

    public event EventHandler? CloseRequested;
    public event EventHandler<string>? CopyToClipboardRequested;
    public event EventHandler<FileTransferConfirmationEventArgs>? FileTransferConfirmationRequested;
    public event EventHandler<FileDownloadConfirmationEventArgs>? FileDownloadConfirmationRequested;

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

        // Subscribe to file transfer events
        this._connection.FileSendRequestReceived += this.OnFileSendRequestReceived;
        this._connection.DirectoryListRequestReceived += this.OnDirectoryListRequestReceived;
        this._connection.FileDownloadRequestReceived += this.OnFileDownloadRequestReceived;

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

    #region File Transfer (Viewer uploads to Presenter)
    private void OnFileSendRequestReceived(object? sender, FileSendRequestReceivedEventArgs e)
    {
        this._logger.LogInformation("Received file upload request: {FileName} ({FileSize} bytes) from {ClientId}",
            e.FileName, e.FileSize, e.SenderClientId);

        Dispatcher.UIThread.Post(() =>
        {
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
        var transfer = new IncomingFileTransfer(
            transferId,
            fileName,
            fileSize,
            this._connection,
            sendAcceptResponse: () => this._connection.SendFileSendResponseAsync(transferId, accepted: true, error: null, senderClientId));

        transfer.Completed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            this.ActiveUploads.Remove(transfer);
            this.Toasts.Success($"File received: {transfer.FileName}");
            transfer.Dispose();
        });

        transfer.Failed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            this.ActiveUploads.Remove(transfer);
            this.Toasts.Error($"Upload failed: {transfer.ErrorMessage ?? "Unknown error"}");
            transfer.Dispose();
        });

        this.ActiveUploads.Add(transfer);
        await transfer.AcceptAsync();

        this._logger.LogInformation("Accepted file upload: {TransferId} -> {DestinationPath}", transferId, transfer.DestinationPath);
    }

    public async Task RejectFileTransferAsync(string senderClientId, string transferId)
    {
        await this._connection.SendFileSendResponseAsync(transferId, accepted: false, error: "Transfer rejected by user", senderClientId);
        this._logger.LogInformation("Rejected file upload: {TransferId}", transferId);
    }

    [RelayCommand]
    private async Task CancelUpload(IncomingFileTransfer transfer)
    {
        await transfer.CancelAsync();
        this.ActiveUploads.Remove(transfer);
        transfer.Dispose();
    }
    #endregion

    #region File Download (Viewer downloads from Presenter)
    private async void OnDirectoryListRequestReceived(object? sender, DirectoryListRequestReceivedEventArgs e)
    {
        this._logger.LogDebug("Directory list request from {ClientId}: {Path}", e.SenderClientId, e.Path);

        try
        {
            DirectoryEntry[] entries;
            string path;

            if (string.IsNullOrEmpty(e.Path))
            {
                // Return root drives
                path = "";
                entries = this._fileSystemService.GetRootPaths()
                    .Select(p => new DirectoryEntry(p, p, IsDirectory: true, 0))
                    .ToArray();
            }
            else
            {
                path = e.Path;
                entries = this._fileSystemService.GetDirectoryEntries(e.Path);
            }

            await this._connection.SendDirectoryListResponseAsync(e.RequestId, path, entries, null, e.SenderClientId);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to list directory: {Path}", e.Path);
            await this._connection.SendDirectoryListResponseAsync(e.RequestId, e.Path, [], ex.Message, e.SenderClientId);
        }
    }

    private void OnFileDownloadRequestReceived(object? sender, FileDownloadRequestReceivedEventArgs e)
    {
        this._logger.LogInformation("Received file download request: {FilePath} from {ClientId}",
            e.FilePath, e.SenderClientId);

        Dispatcher.UIThread.Post(() =>
        {
            var args = new FileDownloadConfirmationEventArgs(
                e.SenderClientId,
                e.TransferId,
                e.FilePath);

            this.FileDownloadConfirmationRequested?.Invoke(this, args);
        });
    }

    public async Task AcceptFileDownloadAsync(string requesterClientId, string transferId, string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                await this._connection.SendFileDownloadResponseAsync(transferId, false, null, null, "File not found", requesterClientId);
                return;
            }

            if (!this._fileSystemService.IsPathAllowed(filePath))
            {
                await this._connection.SendFileDownloadResponseAsync(transferId, false, null, null, "Access denied", requesterClientId);
                return;
            }

            var fileInfo = new FileInfo(filePath);

            // Send acceptance with file info
            await this._connection.SendFileDownloadResponseAsync(
                transferId, true, fileInfo.Name, fileInfo.Length, null, requesterClientId);

            // Create and start the download transfer (presenter sends to specific viewer)
            var transfer = new OutgoingFileTransfer(
                filePath,
                this._connection,
                sendChunk: chunk => this._connection.SendFileChunkToViewerAsync(chunk, requesterClientId),
                sendComplete: tid => this._connection.SendFileCompleteToViewerAsync(tid, requesterClientId),
                requiresAcceptance: false,
                transferId: transferId);

            transfer.Completed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                this.ActiveDownloads.Remove(transfer);
                this.Toasts.Success($"File sent: {transfer.FileName}");
                transfer.Dispose();
            });

            transfer.Failed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                this.ActiveDownloads.Remove(transfer);
                this.Toasts.Error($"Download failed: {transfer.ErrorMessage ?? "Unknown error"}");
                transfer.Dispose();
            });

            this.ActiveDownloads.Add(transfer);
            _ = Task.Run(transfer.StartAsync);

            this._logger.LogInformation("Started serving download: {FilePath} -> {ClientId}", filePath, requesterClientId);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to accept download request for {FilePath}", filePath);
            await this._connection.SendFileDownloadResponseAsync(transferId, false, null, null, ex.Message, requesterClientId);
        }
    }

    public async Task RejectFileDownloadAsync(string requesterClientId, string transferId)
    {
        await this._connection.SendFileDownloadResponseAsync(transferId, false, null, null, "Download rejected by presenter", requesterClientId);
        this._logger.LogInformation("Rejected file download: {TransferId}", transferId);
    }

    [RelayCommand]
    private async Task CancelDownload(OutgoingFileTransfer transfer)
    {
        await transfer.CancelAsync();
        this.ActiveDownloads.Remove(transfer);
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

        // Cancel and dispose all active uploads
        foreach (var transfer in this.ActiveUploads.ToList())
        {
            await transfer.CancelAsync();
            transfer.Dispose();
        }
        this.ActiveUploads.Clear();

        // Cancel and dispose all active downloads
        foreach (var transfer in this.ActiveDownloads.ToList())
        {
            await transfer.CancelAsync();
            transfer.Dispose();
        }
        this.ActiveDownloads.Clear();

        this._captureManager?.Dispose();
        await this._connection.DisconnectAsync();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.Closed -= this.OnConnectionClosed;
        this._hubClient.CredentialsAssigned -= this.OnCredentialsAssigned;
        this._connection.FileSendRequestReceived -= this.OnFileSendRequestReceived;
        this._connection.DirectoryListRequestReceived -= this.OnDirectoryListRequestReceived;
        this._connection.FileDownloadRequestReceived -= this.OnFileDownloadRequestReceived;

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

public sealed class FileDownloadConfirmationEventArgs : EventArgs
{
    public FileDownloadConfirmationEventArgs(string requesterClientId, string transferId, string filePath)
    {
        this.RequesterClientId = requesterClientId;
        this.TransferId = transferId;
        this.FilePath = filePath;
    }

    public string RequesterClientId { get; }
    public string TransferId { get; }
    public string FilePath { get; }
}

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.FileSystem;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public sealed class FileTransferService : IDisposable
{
    private readonly Connection _connection;
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger<FileTransferService> _logger;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DirectoryBrowseResult>> _pendingDirectoryRequests = new();
    private readonly ConcurrentDictionary<string, byte> _cancelledTransfers = new();
    private bool _disposed;

    public ToastsViewModel? Toasts { get; set; }

    public FileTransferService(Connection connection, IFileSystemService fileSystemService, ILogger<FileTransferService> logger)
    {
        this._connection = connection;
        this._fileSystemService = fileSystemService;
        this._logger = logger;

        // Subscribe to incoming requests (presenter receives these)
        this._connection.FileSendRequestReceived += this.OnFileSendRequestReceived;
        this._connection.FileDownloadRequestReceived += this.OnFileDownloadRequestReceived;

        // Directory browsing
        this._connection.DirectoryListResponseReceived += this.OnDirectoryListResponseReceived;
        this._connection.DirectoryListRequestReceived += this.OnDirectoryListRequestReceived;

        // Track cancelled transfers that arrive before the operation is created
        this._connection.FileCancelReceived += this.OnFileCancelReceivedForPending;
    }

    public ObservableCollection<IFileTransfer> ActiveSends { get; } = [];
    public ObservableCollection<IFileTransfer> ActiveReceives { get; } = [];

    // Events for transfer completion
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferFailedEventArgs>? TransferFailed;

    #region Directory Browsing (Viewer Operations)

    /// <summary>
    /// Browses a remote directory on the presenter's machine. Used by viewers.
    /// </summary>
    public async Task<DirectoryBrowseResult> BrowseDirectoryAsync(string path = "", TimeSpan? timeout = null)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<DirectoryBrowseResult>();
        this._pendingDirectoryRequests[requestId] = tcs;

        try
        {
            await this._connection.SendDirectoryListRequestAsync(requestId, path);

            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
            cts.Token.Register(() => tcs.TrySetResult(DirectoryBrowseResult.TimedOut()));

            return await tcs.Task;
        }
        finally
        {
            this._pendingDirectoryRequests.TryRemove(requestId, out _);
        }
    }
    private void OnDirectoryListResponseReceived(object? sender, DirectoryListResponseReceivedEventArgs e)
    {
        if (this._pendingDirectoryRequests.TryRemove(e.RequestId, out var tcs))
        {
            if (e.ErrorMessage is not null)
            {
                tcs.TrySetResult(DirectoryBrowseResult.Failed(e.ErrorMessage));
            }
            else
            {
                tcs.TrySetResult(DirectoryBrowseResult.Success(e.Path, e.Entries));
            }
        }
    }

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

    #endregion

    #region File Transfer - Viewer Operations

    /// <summary>
    /// Sends a file to the presenter. Used by viewers.
    /// </summary>
    public async Task<FileSendOperation> SendFileAsync(string filePath)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var transfer = new FileSendOperation(
            transferId,
            filePath,
            this._connection,
            sendChunk: chunk => this._connection.SendFileChunkAsync(chunk),
            sendComplete: tid => this._connection.SendFileCompleteAsync(tid),
            sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason),
            sendError: (tid, error) => this._connection.SendFileErrorAsync(tid, error),
            requiresAcceptance: true);

        this.TrackSend(transfer);
        await transfer.StartAsync();
        return transfer;
    }

    /// <summary>
    /// Requests a file download from the presenter. Used by viewers.
    /// </summary>
    public async Task<FileReceiveOperation> RequestDownloadAsync(string remotePath, string fileName, long fileSize)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var transfer = new FileReceiveOperation(
            transferId,
            fileName,
            fileSize,
            this._connection,
            sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason),
            sendRequest: () => this._connection.SendFileDownloadRequestAsync(transferId, remotePath));

        this.TrackReceive(transfer);
        await transfer.StartAsync();
        return transfer;
    }

    #endregion

    #region Collection Management

    private void TrackSend(FileSendOperation transfer)
    {
        transfer.Completed += this.OnTransferCompleted;
        transfer.Failed += this.OnTransferFailed;

        Dispatcher.UIThread.Post(() => this.ActiveSends.Add(transfer));
    }

    private void TrackReceive(FileReceiveOperation transfer)
    {
        transfer.Completed += this.OnTransferCompleted;
        transfer.Failed += this.OnTransferFailed;

        Dispatcher.UIThread.Post(() => this.ActiveReceives.Add(transfer));
    }

    private void OnTransferCompleted(object? sender, EventArgs e)
    {
        if (sender is not IFileTransfer transfer)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            this.ActiveSends.Remove(transfer);
            this.ActiveReceives.Remove(transfer);
            transfer.Dispose();
        });

        this.TransferCompleted?.Invoke(this, new TransferCompletedEventArgs(transfer));
    }

    private void OnTransferFailed(object? sender, EventArgs e)
    {
        if (sender is not IFileTransfer transfer)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            this.ActiveSends.Remove(transfer);
            this.ActiveReceives.Remove(transfer);
            transfer.Dispose();
        });

        this.TransferFailed?.Invoke(this, new TransferFailedEventArgs(transfer));
    }

    #endregion

    #region Incoming Request Handlers

    private void OnFileCancelReceivedForPending(object? sender, FileCancelReceivedEventArgs e)
    {
        this._cancelledTransfers.TryAdd(e.TransferId, 0);
    }

    private void OnFileSendRequestReceived(object? sender, FileSendRequestReceivedEventArgs e)
    {
        var viewer = this._connection.Viewers.FirstOrDefault(v => v.ClientId == e.SenderClientId);
        var displayName = viewer?.DisplayName ?? "Unknown Viewer";

        Dispatcher.UIThread.Post(async () =>
        {
            var fileSizeFormatted = FileTransferHelpers.FormatFileSize(e.FileSize);
            var dialog = FileTransferConfirmationDialog.CreateForUpload(displayName, e.FileName, fileSizeFormatted);
            dialog.Show();

            if (await dialog.ResultTask)
            {
                var transfer = new FileReceiveOperation(
                    e.TransferId,
                    e.FileName,
                    e.FileSize,
                    this._connection,
                    sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, e.SenderClientId),
                    sendAcceptResponse: () => this._connection.SendFileSendResponseAsync(e.TransferId, true, null, e.SenderClientId));

                if (this._cancelledTransfers.TryRemove(e.TransferId, out _))
                {
                    transfer.Dispose();
                    return;
                }

                this.TrackReceive(transfer);
                await transfer.AcceptAsync();
                this.Toasts?.AddTransfer(transfer, isUpload: false);
                this._logger.LogInformation("Accepted file upload: {TransferId} -> {DestinationPath}", e.TransferId, transfer.DestinationPath);
            }
            else
            {
                this._cancelledTransfers.TryRemove(e.TransferId, out _);
                await this._connection.SendFileSendResponseAsync(e.TransferId, false, "Transfer rejected by user", e.SenderClientId);
                this._logger.LogInformation("Rejected file upload: {TransferId}", e.TransferId);
            }
        });
    }

    private void OnFileDownloadRequestReceived(object? sender, FileDownloadRequestReceivedEventArgs e)
    {
        var viewer = this._connection.Viewers.FirstOrDefault(v => v.ClientId == e.SenderClientId);
        var displayName = viewer?.DisplayName ?? "Unknown Viewer";

        Dispatcher.UIThread.Post(async () =>
        {
            var dialog = FileTransferConfirmationDialog.CreateForDownload(displayName, e.FilePath);
            dialog.Show();

            if (await dialog.ResultTask)
            {
                if (!File.Exists(e.FilePath))
                {
                    this._cancelledTransfers.TryRemove(e.TransferId, out _);
                    await this._connection.SendFileDownloadResponseAsync(e.TransferId, false, "File not found", e.SenderClientId);
                    return;
                }

                if (!this._fileSystemService.IsPathAllowed(e.FilePath))
                {
                    this._cancelledTransfers.TryRemove(e.TransferId, out _);
                    await this._connection.SendFileDownloadResponseAsync(e.TransferId, false, "Access denied", e.SenderClientId);
                    return;
                }

                var transfer = new FileSendOperation(
                    e.TransferId,
                    e.FilePath,
                    this._connection,
                    sendChunk: chunk => this._connection.SendFileChunkAsync(chunk, e.SenderClientId),
                    sendComplete: tid => this._connection.SendFileCompleteAsync(tid, e.SenderClientId),
                    sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, e.SenderClientId),
                    sendError: (tid, error) => this._connection.SendFileErrorAsync(tid, error, e.SenderClientId),
                    requiresAcceptance: false);

                if (this._cancelledTransfers.TryRemove(e.TransferId, out _))
                {
                    transfer.Dispose();
                    return;
                }

                this.TrackSend(transfer);
                await this._connection.SendFileDownloadResponseAsync(e.TransferId, true, null, e.SenderClientId);
                _ = transfer.StartAsync();

                this.Toasts?.AddTransfer(transfer, isUpload: true);
                this._logger.LogInformation("Started serving download: {FilePath} -> {ClientId}", e.FilePath, e.SenderClientId);
            }
            else
            {
                this._cancelledTransfers.TryRemove(e.TransferId, out _);
                await this._connection.SendFileDownloadResponseAsync(e.TransferId, false, "Download rejected by presenter", e.SenderClientId);
                this._logger.LogInformation("Rejected file download: {TransferId}", e.TransferId);
            }
        });
    }

    #endregion

    #region Cleanup

    public async Task CancelAllAsync()
    {
        foreach (var transfer in this.ActiveSends.ToList())
        {
            await transfer.CancelAsync();
            transfer.Dispose();
        }
        this.ActiveSends.Clear();

        foreach (var transfer in this.ActiveReceives.ToList())
        {
            await transfer.CancelAsync();
            transfer.Dispose();
        }
        this.ActiveReceives.Clear();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._connection.FileSendRequestReceived -= this.OnFileSendRequestReceived;
        this._connection.FileDownloadRequestReceived -= this.OnFileDownloadRequestReceived;
        this._connection.DirectoryListResponseReceived -= this.OnDirectoryListResponseReceived;
        this._connection.DirectoryListRequestReceived -= this.OnDirectoryListRequestReceived;
        this._connection.FileCancelReceived -= this.OnFileCancelReceivedForPending;

        // Cancel any pending directory requests
        foreach (var tcs in this._pendingDirectoryRequests.Values)
        {
            tcs.TrySetCanceled();
        }
        this._pendingDirectoryRequests.Clear();

        foreach (var transfer in this.ActiveSends)
            transfer.Dispose();
        this.ActiveSends.Clear();

        foreach (var transfer in this.ActiveReceives)
            transfer.Dispose();
        this.ActiveReceives.Clear();
    }

    #endregion
}

#region Types

public sealed class TransferCompletedEventArgs : EventArgs
{
    public TransferCompletedEventArgs(IFileTransfer transfer)
    {
        this.Transfer = transfer;
    }

    public IFileTransfer Transfer { get; }
}

public sealed class TransferFailedEventArgs : EventArgs
{
    public TransferFailedEventArgs(IFileTransfer transfer)
    {
        this.Transfer = transfer;
    }

    public IFileTransfer Transfer { get; }
}

public sealed class DirectoryBrowseResult
{
    private DirectoryBrowseResult(bool isSuccess, string path, DirectoryEntry[] entries, string? errorMessage)
    {
        this.IsSuccess = isSuccess;
        this.Path = path;
        this.Entries = entries;
        this.ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public string Path { get; }
    public DirectoryEntry[] Entries { get; }
    public string? ErrorMessage { get; }

    public static DirectoryBrowseResult Success(string path, DirectoryEntry[] entries)
        => new(true, path, entries, null);

    public static DirectoryBrowseResult Failed(string errorMessage)
        => new(false, "", [], errorMessage);

    public static DirectoryBrowseResult TimedOut()
        => new(false, "", [], "Request timed out");
}

#endregion

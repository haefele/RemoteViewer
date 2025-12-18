using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
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
    private bool _disposed;

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
    }

    public ObservableCollection<IFileTransfer> ActiveSends { get; } = [];
    public ObservableCollection<IFileTransfer> ActiveReceives { get; } = [];

    // Events for when confirmation is needed (presenter side)
    public event EventHandler<IncomingFileRequestedEventArgs>? IncomingFileRequested;
    public event EventHandler<DownloadRequestedEventArgs>? DownloadRequested;

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

    #region Presenter Operations

    /// <summary>
    /// Accepts an incoming file upload from a viewer. Used by presenters.
    /// </summary>
    public async Task<FileReceiveOperation> AcceptIncomingFileAsync(string senderClientId, string transferId, string fileName, long fileSize)
    {
        var transfer = new FileReceiveOperation(
            transferId,
            fileName,
            fileSize,
            this._connection,
            sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, senderClientId),
            sendAcceptResponse: () => this._connection.SendFileSendResponseAsync(transferId, accepted: true, error: null, senderClientId));

        this.TrackReceive(transfer);
        await transfer.AcceptAsync();
        return transfer;
    }

    /// <summary>
    /// Rejects an incoming file upload from a viewer. Used by presenters.
    /// </summary>
    public async Task RejectIncomingFileAsync(string senderClientId, string transferId)
    {
        await this._connection.SendFileSendResponseAsync(transferId, accepted: false, error: "Transfer rejected by user", senderClientId);
    }

    /// <summary>
    /// Accepts a download request from a viewer and starts sending the file. Used by presenters.
    /// </summary>
    public async Task<FileSendOperation> AcceptDownloadRequestAsync(string requesterClientId, string transferId, string filePath)
    {
        // Create and start the transfer
        var transfer = new FileSendOperation(
            transferId,
            filePath,
            this._connection,
            sendChunk: chunk => this._connection.SendFileChunkAsync(chunk, requesterClientId),
            sendComplete: tid => this._connection.SendFileCompleteAsync(tid, requesterClientId),
            sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, requesterClientId),
            sendError: (tid, error) => this._connection.SendFileErrorAsync(tid, error, requesterClientId),
            requiresAcceptance: false);

        this.TrackSend(transfer);

        // Send acceptance
        await this._connection.SendFileDownloadResponseAsync(transferId, true, null, requesterClientId);

        // Start sending the file
        _ = transfer.StartAsync();

        return transfer;
    }

    /// <summary>
    /// Rejects a download request from a viewer. Used by presenters.
    /// </summary>
    public async Task RejectDownloadRequestAsync(string requesterClientId, string transferId, string? reason = null)
    {
        await this._connection.SendFileDownloadResponseAsync(transferId, false, reason ?? "Download rejected by presenter", requesterClientId);
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

    private void OnFileSendRequestReceived(object? sender, FileSendRequestReceivedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.IncomingFileRequested?.Invoke(this, new IncomingFileRequestedEventArgs(
                e.SenderClientId,
                e.TransferId,
                e.FileName,
                e.FileSize));
        });
    }

    private void OnFileDownloadRequestReceived(object? sender, FileDownloadRequestReceivedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.DownloadRequested?.Invoke(this, new DownloadRequestedEventArgs(
                e.SenderClientId,
                e.TransferId,
                e.FilePath));
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

#region Event Args

public sealed class IncomingFileRequestedEventArgs : EventArgs
{
    public IncomingFileRequestedEventArgs(string senderClientId, string transferId, string fileName, long fileSize)
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

public sealed class DownloadRequestedEventArgs : EventArgs
{
    public DownloadRequestedEventArgs(string requesterClientId, string transferId, string filePath)
    {
        this.RequesterClientId = requesterClientId;
        this.TransferId = transferId;
        this.FilePath = filePath;
    }

    public string RequesterClientId { get; }
    public string TransferId { get; }
    public string FilePath { get; }
}

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

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public sealed class FileTransferService : IDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<FileTransferService> _logger;
    private readonly ConcurrentDictionary<string, byte> _cancelledTransfers = new();
    private bool _disposed;

    public ToastsViewModel? Toasts { get; set; }

    public FileTransferService(Connection connection, ILogger<FileTransferService> logger)
    {
        this._connection = connection;
        this._logger = logger;

        // Subscribe to incoming requests
        this._connection.FileSendRequestReceived += this.OnFileSendRequestReceived;

        // Track cancelled transfers that arrive before the operation is created
        this._connection.FileCancelReceived += this.OnFileCancelReceivedForPending;
    }

    public ObservableCollection<IFileTransfer> ActiveSends { get; } = [];
    public ObservableCollection<IFileTransfer> ActiveReceives { get; } = [];

    // Events for transfer completion
    public event EventHandler<TransferCompletedEventArgs>? TransferCompleted;
    public event EventHandler<TransferFailedEventArgs>? TransferFailed;

    #region File Transfer Operations

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
    /// Sends a file to a specific viewer. Used by presenters.
    /// </summary>
    public async Task<FileSendOperation> SendFileToViewerAsync(string filePath, string targetClientId)
    {
        var transferId = Guid.NewGuid().ToString("N");
        var transfer = new FileSendOperation(
            transferId,
            filePath,
            this._connection,
            sendChunk: chunk => this._connection.SendFileChunkAsync(chunk, targetClientId),
            sendComplete: tid => this._connection.SendFileCompleteAsync(tid, targetClientId),
            sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, targetClientId),
            sendError: (tid, error) => this._connection.SendFileErrorAsync(tid, error, targetClientId),
            requiresAcceptance: true,
            targetClientId: targetClientId);

        this.TrackSend(transfer);
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
        Dispatcher.UIThread.Post(async () =>
        {
            var displayName = this._connection.IsPresenter
                ? this._connection.Viewers.FirstOrDefault(v => v.ClientId == e.SenderClientId)?.DisplayName ?? "Unknown Viewer"
                : this._connection.Presenter?.DisplayName ?? "Presenter";
            var fileSizeFormatted = FileTransferHelpers.FormatFileSize(e.FileSize);
            var dialog = FileTransferConfirmationDialog.CreateForUpload(displayName, e.FileName, fileSizeFormatted);

            dialog.Show();

            if (await dialog.ResultTask)
            {
                var targetClientId = this._connection.IsPresenter ? e.SenderClientId : null;

                var transfer = new FileReceiveOperation(
                    e.TransferId,
                    e.FileName,
                    e.FileSize,
                    this._connection,
                    sendCancel: (tid, reason) => this._connection.SendFileCancelAsync(tid, reason, targetClientId),
                    sendAcceptResponse: () => this._connection.SendFileSendResponseAsync(e.TransferId, true, null, targetClientId));

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
                var rejectTargetClientId = this._connection.IsPresenter ? e.SenderClientId : null;
                await this._connection.SendFileSendResponseAsync(e.TransferId, false, "Transfer rejected by user", rejectTargetClientId);
                this._logger.LogInformation("Rejected file upload: {TransferId}", e.TransferId);
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
        this._connection.FileCancelReceived -= this.OnFileCancelReceivedForPending;

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

#endregion

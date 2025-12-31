using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public sealed class FileTransferService : IFileTransferServiceImpl, IDisposable
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
            this,
            sendChunk: chunk => ((IConnectionImpl)this._connection).SendFileChunkAsync(chunk, null),
            sendComplete: tid => ((IConnectionImpl)this._connection).SendFileCompleteAsync(tid, null),
            sendCancel: (tid, reason) => ((IConnectionImpl)this._connection).SendFileCancelAsync(tid, reason, null),
            sendError: (tid, error) => ((IConnectionImpl)this._connection).SendFileErrorAsync(tid, error, null),
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
            this,
            sendChunk: chunk => ((IConnectionImpl)this._connection).SendFileChunkAsync(chunk, targetClientId),
            sendComplete: tid => ((IConnectionImpl)this._connection).SendFileCompleteAsync(tid, targetClientId),
            sendCancel: (tid, reason) => ((IConnectionImpl)this._connection).SendFileCancelAsync(tid, reason, targetClientId),
            sendError: (tid, error) => ((IConnectionImpl)this._connection).SendFileErrorAsync(tid, error, targetClientId),
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

    #region IFileTransferServiceImpl Implementation

    void IFileTransferServiceImpl.HandleFileSendRequest(string senderClientId, string transferId, string fileName, long fileSize)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            var displayName = this._connection.IsPresenter
                ? this._connection.Viewers.FirstOrDefault(v => v.ClientId == senderClientId)?.DisplayName ?? "Unknown Viewer"
                : this._connection.Presenter?.DisplayName ?? "Presenter";
            var fileSizeFormatted = FileTransferHelpers.FormatFileSize(fileSize);
            var dialog = FileTransferConfirmationDialog.AskForConfirmation(displayName, fileName, fileSizeFormatted);

            dialog.Show();

            var targetClientId = this._connection.IsPresenter ? senderClientId : null;

            if (await dialog.ResultTask)
            {
                var transfer = new FileReceiveOperation(
                    transferId,
                    fileName,
                    fileSize,
                    this,
                    this._logger,
                    sendCancel: (tid, reason) => ((IConnectionImpl)this._connection).SendFileCancelAsync(tid, reason, targetClientId));

                if (this._cancelledTransfers.TryRemove(transferId, out _))
                {
                    transfer.Dispose();
                    return;
                }

                this.TrackReceive(transfer);
                await transfer.AcceptAsync();
                this.Toasts?.AddTransfer(transfer, isUpload: false);
                await ((IConnectionImpl)this._connection).SendFileSendResponseAsync(transferId, true, null, targetClientId);
                this._logger.LogInformation("Accepted file upload: {TransferId} -> {DestinationPath}", transferId, transfer.DestinationPath);
            }
            else
            {
                this._cancelledTransfers.TryRemove(transferId, out _);
                await ((IConnectionImpl)this._connection).SendFileSendResponseAsync(transferId, false, "The recipient declined the file transfer.", targetClientId);
                this._logger.LogInformation("Rejected file upload: {TransferId}", transferId);
            }
        });
    }

    void IFileTransferServiceImpl.HandleFileSendResponse(string transferId, bool accepted, string? errorMessage)
    {
        this.FileSendResponseReceived?.Invoke(transferId, accepted, errorMessage);
    }

    void IFileTransferServiceImpl.HandleFileChunk(string senderClientId, FileChunkMessage chunk)
    {
        this.FileChunkReceived?.Invoke(senderClientId, chunk);
    }

    void IFileTransferServiceImpl.HandleFileComplete(string senderClientId, string transferId)
    {
        this.FileCompleteReceived?.Invoke(senderClientId, transferId);
    }

    void IFileTransferServiceImpl.HandleFileCancel(string senderClientId, string transferId, string reason)
    {
        this._cancelledTransfers.TryAdd(transferId, 0);
        this.FileCancelReceived?.Invoke(senderClientId, transferId, reason);
    }

    void IFileTransferServiceImpl.HandleFileError(string senderClientId, string transferId, string errorMessage)
    {
        this.FileErrorReceived?.Invoke(senderClientId, transferId, errorMessage);
    }

    // Internal events for routing to operations (temporary - operations will be updated later)
    internal event Action<string, bool, string?>? FileSendResponseReceived;
    internal event Action<string, FileChunkMessage>? FileChunkReceived;
    internal event Action<string, string>? FileCompleteReceived;
    internal event Action<string, string, string>? FileCancelReceived;
    internal event Action<string, string, string>? FileErrorReceived;

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

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class FileReceiveOperation : ObservableObject, IFileTransfer
{
    private readonly FileTransferService _fileTransferService;
    private readonly ILogger _logger;
    private readonly Func<string, string, Task> _sendCancel;
    private FileStream? _fileStream;
    private bool _disposed;

    public FileReceiveOperation(
        string transferId,
        string fileName,
        long fileSize,
        FileTransferService fileTransferService,
        ILogger logger,
        Func<string, string, Task> sendCancel)
    {
        this._fileTransferService = fileTransferService;
        this._logger = logger;
        this._sendCancel = sendCancel;

        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;
        this.SetupDestinationPath(fileName);

        this._fileTransferService.FileCancelReceived += this.OnFileCancelReceived;
    }

    public string TransferId { get; }

    [ObservableProperty]
    private string? _fileName;

    [ObservableProperty]
    private long _fileSize;

    public string TempPath { get; private set; } = string.Empty;
    public string DestinationPath { get; private set; } = string.Empty;

    [ObservableProperty]
    private int _chunksReceived;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercent))]
    private double _progress;

    [ObservableProperty]
    private FileTransferState _state = FileTransferState.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    public string FileSizeFormatted => FileTransferHelpers.FormatFileSize(this.FileSize);
    public int ProgressPercent => (int)(this.Progress * 100);

    public event EventHandler? Completed;
    public event EventHandler? Failed;

    /// <summary>
    /// Accepts an incoming file transfer.
    /// </summary>
    public async Task AcceptAsync()
    {
        if (this.State != FileTransferState.Pending)
            return;

        this.SubscribeToChunkEvents();
        this.OpenTempFile();
        this.State = FileTransferState.Transferring;
    }

    public async Task CancelAsync()
    {
        if (this.State is FileTransferState.Completed or FileTransferState.Failed or FileTransferState.Cancelled or FileTransferState.Rejected)
            return;

        this.State = FileTransferState.Cancelled;
        this.ErrorMessage = "The file transfer was cancelled.";
        await this._sendCancel(this.TransferId, this.ErrorMessage);
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void SubscribeToChunkEvents()
    {
        this._fileTransferService.FileChunkReceived += this.OnFileChunkReceived;
        this._fileTransferService.FileCompleteReceived += this.OnFileCompleteReceived;
        this._fileTransferService.FileErrorReceived += this.OnFileErrorReceived;
    }

    private void SetupDestinationPath(string fileName)
    {
        var downloadsPath = FileTransferHelpers.GetDownloadsFolder();
        this.DestinationPath = FileTransferHelpers.GetUniqueFilePath(downloadsPath, fileName);
        this.TempPath = this.DestinationPath + ".part";
    }

    private void OpenTempFile()
    {
        this._fileStream = new FileStream(this.TempPath, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    private void OnFileChunkReceived(string senderClientId, FileChunkMessage chunk)
    {
        if (chunk.TransferId != this.TransferId)
            return;

        if (this._fileStream is null || this.State != FileTransferState.Transferring)
            return;

        try
        {
            this._fileStream.Write(chunk.Data.Span);
            this.ChunksReceived++;
            this.Progress = (double)this.ChunksReceived / chunk.TotalChunks;
        }
        catch (Exception ex)
        {
            this.State = FileTransferState.Failed;
            this.ErrorMessage = ex.Message;
            this.DeleteTempFile();
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFileCompleteReceived(string senderClientId, string transferId)
    {
        if (transferId != this.TransferId)
            return;

        try
        {
            this._fileStream?.Dispose();
            this._fileStream = null;

            if (File.Exists(this.TempPath))
            {
                File.Move(this.TempPath, this.DestinationPath, overwrite: true);
            }

            this.State = FileTransferState.Completed;
            this.Progress = 1.0;
            this.Cleanup();
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            this.State = FileTransferState.Failed;
            this.ErrorMessage = ex.Message;
            this.DeleteTempFile();
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFileCancelReceived(string senderClientId, string transferId, string reason)
    {
        if (transferId != this.TransferId)
            return;

        this.State = FileTransferState.Cancelled;
        this.ErrorMessage = reason;
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(string senderClientId, string transferId, string errorMessage)
    {
        if (transferId != this.TransferId)
            return;

        this.State = FileTransferState.Failed;
        this.ErrorMessage = errorMessage;
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void DeleteTempFile()
    {
        this._fileStream?.Dispose();
        this._fileStream = null;

        try
        {
            if (!string.IsNullOrEmpty(this.TempPath) && File.Exists(this.TempPath))
                File.Delete(this.TempPath);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to delete temp file: {TempPath}", this.TempPath);
        }
    }

    private void Cleanup()
    {
        this._fileStream?.Dispose();
        this._fileStream = null;

        this._fileTransferService.FileChunkReceived -= this.OnFileChunkReceived;
        this._fileTransferService.FileCompleteReceived -= this.OnFileCompleteReceived;
        this._fileTransferService.FileCancelReceived -= this.OnFileCancelReceived;
        this._fileTransferService.FileErrorReceived -= this.OnFileErrorReceived;
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this.Cleanup();
        GC.SuppressFinalize(this);
    }
}

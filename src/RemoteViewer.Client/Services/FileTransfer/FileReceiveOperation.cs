using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.HubClient;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class FileReceiveOperation : ObservableObject, IFileTransfer
{
    private readonly Connection _connection;
    private readonly Func<Task>? _sendRequest;
    private readonly Func<Task>? _sendAcceptResponse;
    private readonly Func<string, string, Task> _sendCancel;
    private FileStream? _fileStream;
    private bool _disposed;

    public FileReceiveOperation(
        string transferId,
        string fileName,
        long fileSize,
        Connection connection,
        Func<string, string, Task> sendCancel,
        Func<Task>? sendRequest = null,
        Func<Task>? sendAcceptResponse = null)
    {
        this._connection = connection;
        this._sendRequest = sendRequest;
        this._sendAcceptResponse = sendAcceptResponse;
        this._sendCancel = sendCancel;

        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;
        this.SetupDestinationPath(fileName);

        if (sendRequest is not null)
        {
            this._connection.FileDownloadResponseReceived += this.OnFileDownloadResponseReceived;
        }
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
    /// Starts by sending a request (for download scenario). Waits for response.
    /// </summary>
    public async Task StartAsync()
    {
        if (this.State != FileTransferState.Pending || this._sendRequest is null)
            return;

        await this._sendRequest();
        // Will continue in OnFileDownloadResponseReceived
    }

    /// <summary>
    /// Accepts an incoming transfer (for upload scenario).
    /// </summary>
    public async Task AcceptAsync()
    {
        if (this.State != FileTransferState.Pending)
            return;

        this.SubscribeToChunkEvents();
        this.OpenTempFile();
        this.State = FileTransferState.Transferring;

        if (this._sendAcceptResponse is not null)
        {
            await this._sendAcceptResponse();
        }
    }

    public async Task CancelAsync()
    {
        if (this.State is FileTransferState.Completed or FileTransferState.Failed or FileTransferState.Cancelled or FileTransferState.Rejected)
            return;

        this.State = FileTransferState.Cancelled;
        this.ErrorMessage = "Cancelled by user";
        await this._sendCancel(this.TransferId, this.ErrorMessage);
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileDownloadResponseReceived(object? sender, FileDownloadResponseReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        if (e.Accepted)
        {
            this.SubscribeToChunkEvents();
            this.OpenTempFile();
            this.State = FileTransferState.Transferring;
        }
        else
        {
            this.State = FileTransferState.Rejected;
            this.ErrorMessage = e.ErrorMessage ?? "Download rejected";
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SubscribeToChunkEvents()
    {
        this._connection.FileChunkReceived += this.OnFileChunkReceived;
        this._connection.FileCompleteReceived += this.OnFileCompleteReceived;
        this._connection.FileCancelReceived += this.OnFileCancelReceived;
        this._connection.FileErrorReceived += this.OnFileErrorReceived;
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

    private void OnFileChunkReceived(object? sender, FileChunkReceivedEventArgs e)
    {
        if (e.Chunk.TransferId != this.TransferId)
            return;

        if (this._fileStream is null || this.State != FileTransferState.Transferring)
            return;

        try
        {
            this._fileStream.Write(e.Chunk.Data.Span);
            this.ChunksReceived++;
            this.Progress = (double)this.ChunksReceived / e.Chunk.TotalChunks;
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

    private void OnFileCompleteReceived(object? sender, FileCompleteReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
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

    private void OnFileCancelReceived(object? sender, FileCancelReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = FileTransferState.Cancelled;
        this.ErrorMessage = e.Reason;
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(object? sender, FileErrorReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = FileTransferState.Failed;
        this.ErrorMessage = e.ErrorMessage;
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
        catch
        {
            // Ignore deletion errors
        }
    }

    private void Cleanup()
    {
        this._fileStream?.Dispose();
        this._fileStream = null;

        this._connection.FileDownloadResponseReceived -= this.OnFileDownloadResponseReceived;
        this._connection.FileChunkReceived -= this.OnFileChunkReceived;
        this._connection.FileCompleteReceived -= this.OnFileCompleteReceived;
        this._connection.FileCancelReceived -= this.OnFileCancelReceived;
        this._connection.FileErrorReceived -= this.OnFileErrorReceived;
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

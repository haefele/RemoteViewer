using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.HubClient;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class FileReceiveOperation : ObservableObject, IFileTransfer
{
    private readonly Connection _connection;
    private readonly Func<Task>? _sendRequest;
    private readonly Func<Task>? _sendAcceptResponse;
    private readonly bool _metadataKnown;
    private FileStream? _fileStream;
    private bool _disposed;

    private FileReceiveOperation(
        string transferId,
        Connection connection,
        string? fileName,
        long fileSize,
        bool metadataKnown,
        Func<Task>? sendRequest,
        Func<Task>? sendAcceptResponse)
    {
        this._connection = connection;
        this._sendRequest = sendRequest;
        this._sendAcceptResponse = sendAcceptResponse;
        this._metadataKnown = metadataKnown;

        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;

        if (metadataKnown && fileName is not null)
        {
            this.SetupDestinationPath(fileName);
        }

        if (!metadataKnown)
        {
            this._connection.FileDownloadResponseReceived += this.OnFileDownloadResponseReceived;
        }
    }

    /// <summary>
    /// Creates a receive operation for an incoming file where metadata is already known.
    /// Used when accepting an upload from another party.
    /// Call AcceptAsync() to start receiving.
    /// </summary>
    public static FileReceiveOperation ForIncomingFile(
        string transferId,
        string fileName,
        long fileSize,
        Connection connection,
        Func<Task>? sendAcceptResponse = null)
    {
        return new FileReceiveOperation(
            transferId,
            connection,
            fileName,
            fileSize,
            metadataKnown: true,
            sendRequest: null,
            sendAcceptResponse);
    }

    /// <summary>
    /// Creates a receive operation for a download request where metadata will be received in response.
    /// Used when requesting a file from another party.
    /// Call StartAsync() to send the request and wait for metadata.
    /// </summary>
    public static FileReceiveOperation ForDownloadRequest(
        string transferId,
        Connection connection,
        Func<Task> sendRequest)
    {
        return new FileReceiveOperation(
            transferId,
            connection,
            fileName: null,
            fileSize: 0,
            metadataKnown: false,
            sendRequest,
            sendAcceptResponse: null);
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
    /// Starts by sending a request (for download scenario). Waits for response with metadata.
    /// </summary>
    public async Task StartAsync()
    {
        if (this.State != FileTransferState.Pending || this._sendRequest is null)
            return;

        await this._sendRequest();
        // Will continue in OnFileDownloadResponseReceived
    }

    /// <summary>
    /// Accepts an incoming transfer (for upload scenario). Metadata must already be known.
    /// </summary>
    public async Task AcceptAsync()
    {
        if (this.State != FileTransferState.Pending || !this._metadataKnown)
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
        await this._connection.SendFileCancelAsync(this.TransferId, "Cancelled by user");
        this.DeleteTempFile();
        this.Cleanup();
    }

    private void OnFileDownloadResponseReceived(object? sender, FileDownloadResponseReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        if (e.Accepted && e.FileName is not null && e.FileSize.HasValue)
        {
            this.FileName = e.FileName;
            this.FileSize = e.FileSize.Value;
            this.SetupDestinationPath(e.FileName);

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

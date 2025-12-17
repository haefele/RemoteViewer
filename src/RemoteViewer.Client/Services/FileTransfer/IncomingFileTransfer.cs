using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.HubClient;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class IncomingFileTransfer : ObservableObject, IDisposable
{
    private readonly Connection _connection;
    private readonly Func<Task>? _sendRequest;
    private readonly Func<Task>? _sendAcceptResponse;
    private FileStream? _fileStream;
    private bool _disposed;
    private bool _metadataKnown;

    /// <summary>
    /// Creates an incoming file transfer where metadata is already known (e.g., upload from viewer).
    /// Call AcceptAsync() to start receiving.
    /// </summary>
    public IncomingFileTransfer(
        string transferId,
        string fileName,
        long fileSize,
        Connection connection,
        Func<Task>? sendAcceptResponse = null)
    {
        this._connection = connection;
        this._sendAcceptResponse = sendAcceptResponse;
        this._metadataKnown = true;

        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;

        this.SetupDestinationPath(fileName);
    }

    /// <summary>
    /// Creates an incoming file transfer where metadata will be received in response (e.g., download request).
    /// Call StartAsync() to send the request and wait for metadata.
    /// </summary>
    public IncomingFileTransfer(
        string transferId,
        Connection connection,
        Func<Task> sendRequest)
    {
        this._connection = connection;
        this._sendRequest = sendRequest;
        this._metadataKnown = false;

        this.TransferId = transferId;

        // Subscribe to download response to get metadata
        this._connection.FileDownloadResponseReceived += this.OnFileDownloadResponseReceived;
    }

    public string TransferId { get; }
    public string? FileName { get; private set; }
    public long FileSize { get; private set; }
    public string TempPath { get; private set; } = string.Empty;
    public string DestinationPath { get; private set; } = string.Empty;

    [ObservableProperty]
    private int _chunksReceived;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private FileReceiveState _state = FileReceiveState.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    public string FileSizeFormatted => FormatFileSize(this.FileSize);
    public int ProgressPercent => (int)(this.Progress * 100);

    public event EventHandler? Completed;
    public event EventHandler? Failed;

    /// <summary>
    /// Starts by sending a request (for download scenario). Waits for response with metadata.
    /// </summary>
    public async Task StartAsync()
    {
        if (this.State != FileReceiveState.Pending || this._sendRequest is null)
            return;

        await this._sendRequest();
        // Will continue in OnFileDownloadResponseReceived
    }

    /// <summary>
    /// Accepts an incoming transfer (for upload scenario). Metadata must already be known.
    /// </summary>
    public async Task AcceptAsync()
    {
        if (this.State != FileReceiveState.Pending || !this._metadataKnown)
            return;

        this.SubscribeToChunkEvents();
        this.OpenTempFile();
        this.State = FileReceiveState.Transferring;

        if (this._sendAcceptResponse is not null)
        {
            await this._sendAcceptResponse();
        }
    }

    public async Task CancelAsync()
    {
        if (this.State is FileReceiveState.Completed or FileReceiveState.Failed or FileReceiveState.Cancelled or FileReceiveState.Rejected)
            return;

        this.State = FileReceiveState.Cancelled;
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
            this.State = FileReceiveState.Transferring;
        }
        else
        {
            this.State = FileReceiveState.Rejected;
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
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        this.DestinationPath = GetUniqueFilePath(downloadsPath, fileName);
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

        if (this._fileStream is null || this.State != FileReceiveState.Transferring)
            return;

        try
        {
            this._fileStream.Write(e.Chunk.Data.Span);
            this.ChunksReceived++;
            this.Progress = (double)this.ChunksReceived / e.Chunk.TotalChunks;
        }
        catch (Exception ex)
        {
            this.State = FileReceiveState.Failed;
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

            this.State = FileReceiveState.Completed;
            this.Progress = 1.0;
            this.Cleanup();
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            this.State = FileReceiveState.Failed;
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

        this.State = FileReceiveState.Cancelled;
        this.ErrorMessage = e.Reason;
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(object? sender, FileErrorReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = FileReceiveState.Failed;
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

    private static string GetUniqueFilePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{baseName} ({counter}){extension}");
            counter++;
        }

        return path;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

public enum FileReceiveState
{
    Pending,
    Transferring,
    Completed,
    Failed,
    Cancelled,
    Rejected
}

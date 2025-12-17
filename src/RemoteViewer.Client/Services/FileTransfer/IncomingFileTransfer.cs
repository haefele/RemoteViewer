using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class IncomingFileTransfer : ObservableObject, IDisposable
{
    private readonly Connection _connection;
    private readonly string _senderClientId;
    private FileStream? _fileStream;
    private bool _disposed;

    public IncomingFileTransfer(
        string senderClientId,
        string transferId,
        string fileName,
        long fileSize,
        Connection connection)
    {
        this._senderClientId = senderClientId;
        this._connection = connection;
        this.TransferId = transferId;
        this.FileName = fileName;
        this.FileSize = fileSize;

        // Calculate destination path
        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        this.DestinationPath = GetUniqueFilePath(downloadsPath, fileName);
        this.TempPath = this.DestinationPath + ".part";
    }

    public string TransferId { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public string TempPath { get; }
    public string DestinationPath { get; }

    [ObservableProperty]
    private int _chunksReceived;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private IncomingTransferState _state = IncomingTransferState.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    public string FileSizeFormatted => FormatFileSize(this.FileSize);
    public int ProgressPercent => (int)(this.Progress * 100);

    public event EventHandler? Completed;
    public event EventHandler? Failed;

    public async Task AcceptAsync()
    {
        if (this.State != IncomingTransferState.Pending)
            return;

        // Subscribe to connection events
        this._connection.FileChunkReceived += this.OnFileChunkReceived;
        this._connection.FileCompleteReceived += this.OnFileCompleteReceived;
        this._connection.FileCancelReceived += this.OnFileCancelReceived;
        this._connection.FileErrorReceived += this.OnFileErrorReceived;

        // Open temp file for writing
        this._fileStream = new FileStream(this.TempPath, FileMode.Create, FileAccess.Write, FileShare.None);

        this.State = IncomingTransferState.Transferring;
        await this._connection.SendFileSendResponseAsync(this.TransferId, accepted: true, error: null, this._senderClientId);
    }

    public async Task RejectAsync()
    {
        if (this.State != IncomingTransferState.Pending)
            return;

        this.State = IncomingTransferState.Rejected;
        await this._connection.SendFileSendResponseAsync(this.TransferId, accepted: false, error: "Rejected by user", this._senderClientId);
        this.Cleanup();
    }

    public async Task CancelAsync()
    {
        if (this.State is IncomingTransferState.Completed or IncomingTransferState.Failed or IncomingTransferState.Cancelled or IncomingTransferState.Rejected)
            return;

        this.State = IncomingTransferState.Cancelled;
        await this._connection.SendFileCancelAsync(this.TransferId, "Cancelled by presenter");
        this.DeleteTempFile();
        this.Cleanup();
    }

    private void OnFileChunkReceived(object? sender, FileChunkReceivedEventArgs e)
    {
        if (e.Chunk.TransferId != this.TransferId)
            return;

        if (this._fileStream is null || this.State != IncomingTransferState.Transferring)
            return;

        try
        {
            this._fileStream.Write(e.Chunk.Data.Span);
            this.ChunksReceived++;
            this.Progress = (double)this.ChunksReceived / e.Chunk.TotalChunks;
        }
        catch (Exception ex)
        {
            this.State = IncomingTransferState.Failed;
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
            // Close the stream before moving the file
            this._fileStream?.Dispose();
            this._fileStream = null;

            // Move temp file to final destination
            if (File.Exists(this.TempPath))
            {
                File.Move(this.TempPath, this.DestinationPath, overwrite: true);
            }

            this.State = IncomingTransferState.Completed;
            this.Progress = 1.0;
            this.Cleanup();
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            this.State = IncomingTransferState.Failed;
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

        this.State = IncomingTransferState.Cancelled;
        this.ErrorMessage = e.Reason;
        this.DeleteTempFile();
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(object? sender, FileErrorReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = IncomingTransferState.Failed;
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
            if (File.Exists(this.TempPath))
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

        // Unsubscribe from events
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

public enum IncomingTransferState
{
    Pending,
    Transferring,
    Completed,
    Failed,
    Cancelled,
    Rejected
}

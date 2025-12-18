using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class FileSendOperation : ObservableObject, IFileTransfer
{
    private const int ChunkSize = 256 * 1024; // 256 KB
    private const long MaxBytesPerSecond = 2 * 1024 * 1024; // 2 MB/s bandwidth cap

    private readonly Connection _connection;
    private readonly Func<FileChunkMessage, Task> _sendChunk;
    private readonly Func<string, Task> _sendComplete;
    private readonly Func<string, string, Task> _sendCancel;
    private readonly Func<string, string, Task> _sendError;
    private readonly bool _requiresAcceptance;
    private readonly string? _targetClientId;
    private FileStream? _fileStream;
    private bool _disposed;

    public FileSendOperation(
        string transferId,
        string filePath,
        Connection connection,
        Func<FileChunkMessage, Task> sendChunk,
        Func<string, Task> sendComplete,
        Func<string, string, Task> sendCancel,
        Func<string, string, Task> sendError,
        bool requiresAcceptance = false,
        string? targetClientId = null)
    {
        this._connection = connection;
        this._sendChunk = sendChunk;
        this._sendComplete = sendComplete;
        this._sendCancel = sendCancel;
        this._sendError = sendError;
        this._requiresAcceptance = requiresAcceptance;
        this._targetClientId = targetClientId;

        this.FilePath = filePath;
        this.TransferId = transferId;

        var fileInfo = new FileInfo(filePath);
        this.FileName = fileInfo.Name;
        this.FileSize = fileInfo.Length;
        this.TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / ChunkSize);

        this._connection.FileCancelReceived += this.OnFileCancelReceived;
        this._connection.FileErrorReceived += this.OnFileErrorReceived;

        if (requiresAcceptance)
        {
            this._connection.FileSendResponseReceived += this.OnFileSendResponseReceived;
        }
    }

    public string TransferId { get; }
    public string FilePath { get; }
    public string? FileName { get; }
    public long FileSize { get; }
    public int TotalChunks { get; }

    [ObservableProperty]
    private int _currentChunk;

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

    public async Task StartAsync()
    {
        if (this.State != FileTransferState.Pending)
            return;

        if (this._requiresAcceptance)
        {
            this.State = FileTransferState.WaitingForAcceptance;
            await this._connection.SendFileSendRequestAsync(this.TransferId, this.FileName!, this.FileSize, this._targetClientId);
        }
        else
        {
            this.State = FileTransferState.Transferring;
            await this.SendChunksAsync();
        }
    }

    public async Task CancelAsync()
    {
        if (this.State is FileTransferState.Completed or FileTransferState.Failed or FileTransferState.Cancelled)
            return;

        this.State = FileTransferState.Cancelled;
        this.ErrorMessage = "The file transfer was cancelled.";
        await this._sendCancel(this.TransferId, this.ErrorMessage);
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileSendResponseReceived(object? sender, FileSendResponseReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        if (e.Accepted)
        {
            this.State = FileTransferState.Transferring;
            _ = this.SendChunksAsync();
        }
        else
        {
            this.State = FileTransferState.Rejected;
            this.ErrorMessage = e.ErrorMessage ?? "The recipient declined the file transfer.";
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
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(object? sender, FileErrorReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = FileTransferState.Failed;
        this.ErrorMessage = e.ErrorMessage;
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SendChunksAsync()
    {
        try
        {
            this._fileStream = new FileStream(this.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = RefCountedMemoryOwner<byte>.Create(ChunkSize);

            var delayPerChunk = TimeSpan.FromSeconds((double)ChunkSize / MaxBytesPerSecond);

            while (this.State == FileTransferState.Transferring)
            {
                var chunkStart = Stopwatch.GetTimestamp();

                var bytesRead = await this._fileStream.ReadAtLeastAsync(buffer.Memory, buffer.Length, throwOnEndOfStream: false);
                if (bytesRead == 0)
                    break;

                var chunk = new FileChunkMessage(
                    this.TransferId,
                    this.CurrentChunk,
                    this.TotalChunks,
                    buffer.Memory[..bytesRead]);

                await this._sendChunk(chunk);

                this.CurrentChunk++;
                this.Progress = (double)this.CurrentChunk / this.TotalChunks;

                // Bandwidth throttling: wait for the remainder of the chunk interval
                var elapsed = Stopwatch.GetElapsedTime(chunkStart);
                var sleepTime = delayPerChunk - elapsed;

                if (sleepTime > TimeSpan.Zero && this.State == FileTransferState.Transferring)
                    await Task.Delay(sleepTime);
            }

            if (this.State == FileTransferState.Transferring)
            {
                await this._sendComplete(this.TransferId);
                this.State = FileTransferState.Completed;
                this.Progress = 1.0;
                this.Cleanup();
                this.Completed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            this.State = FileTransferState.Failed;
            this.ErrorMessage = ex.Message;
            await this._sendError(this.TransferId, ex.Message);
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Cleanup()
    {
        this._fileStream?.Dispose();
        this._fileStream = null;

        this._connection.FileCancelReceived -= this.OnFileCancelReceived;
        this._connection.FileErrorReceived -= this.OnFileErrorReceived;

        if (this._requiresAcceptance)
        {
            this._connection.FileSendResponseReceived -= this.OnFileSendResponseReceived;
        }
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

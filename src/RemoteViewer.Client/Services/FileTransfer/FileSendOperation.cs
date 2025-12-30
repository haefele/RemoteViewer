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
    private readonly FileTransferService _fileTransferService;
    private readonly Func<FileChunkMessage, Task> _sendChunk;
    private readonly Func<string, Task> _sendComplete;
    private readonly Func<string, string, Task> _sendCancel;
    private readonly Func<string, string, Task> _sendError;
    private readonly bool _requiresAcceptance;
    private readonly string? _targetClientId;
    private FileStream? _fileStream;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public FileSendOperation(
        string transferId,
        string filePath,
        Connection connection,
        FileTransferService fileTransferService,
        Func<FileChunkMessage, Task> sendChunk,
        Func<string, Task> sendComplete,
        Func<string, string, Task> sendCancel,
        Func<string, string, Task> sendError,
        bool requiresAcceptance = false,
        string? targetClientId = null)
    {
        this._connection = connection;
        this._fileTransferService = fileTransferService;
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

        this._fileTransferService.FileCancelReceived += this.OnFileCancelReceived;
        this._fileTransferService.FileErrorReceived += this.OnFileErrorReceived;

        if (requiresAcceptance)
        {
            this._fileTransferService.FileSendResponseReceived += this.OnFileSendResponseReceived;
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
        this._cts?.Cancel();
        await this._sendCancel(this.TransferId, this.ErrorMessage);
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileSendResponseReceived(string transferId, bool accepted, string? errorMessage)
    {
        if (transferId != this.TransferId)
            return;

        if (accepted)
        {
            this.State = FileTransferState.Transferring;
            _ = this.SendChunksAsync();
        }
        else
        {
            this.State = FileTransferState.Rejected;
            this.ErrorMessage = errorMessage ?? "The recipient declined the file transfer.";
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
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(string senderClientId, string transferId, string errorMessage)
    {
        if (transferId != this.TransferId)
            return;

        this.State = FileTransferState.Failed;
        this.ErrorMessage = errorMessage;
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SendChunksAsync()
    {
        this._cts = new CancellationTokenSource();
        var ct = this._cts.Token;

        try
        {
            this._fileStream = new FileStream(this.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = RefCountedMemoryOwner.Create(ChunkSize);

            var delayPerChunk = TimeSpan.FromSeconds((double)ChunkSize / MaxBytesPerSecond);

            while (this.State == FileTransferState.Transferring)
            {
                ct.ThrowIfCancellationRequested();

                var chunkStart = Stopwatch.GetTimestamp();

                var bytesRead = await this._fileStream.ReadAtLeastAsync(buffer.Memory, buffer.Length, throwOnEndOfStream: false, ct);
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
                    await Task.Delay(sleepTime, ct);
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
        catch (OperationCanceledException)
        {
            // Cancellation is handled by CancelAsync, no need to do anything here
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
        this._cts?.Dispose();
        this._cts = null;

        this._fileStream?.Dispose();
        this._fileStream = null;

        this._fileTransferService.FileCancelReceived -= this.OnFileCancelReceived;
        this._fileTransferService.FileErrorReceived -= this.OnFileErrorReceived;

        if (this._requiresAcceptance)
        {
            this._fileTransferService.FileSendResponseReceived -= this.OnFileSendResponseReceived;
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

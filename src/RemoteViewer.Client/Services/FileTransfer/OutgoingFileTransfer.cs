using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

public partial class OutgoingFileTransfer : ObservableObject, IDisposable
{
    private const int ChunkSize = 256 * 1024; // 256 KB

    private readonly Connection _connection;
    private FileStream? _fileStream;
    private bool _disposed;

    public OutgoingFileTransfer(string filePath, Connection connection)
    {
        this._connection = connection;
        this.FilePath = filePath;
        this.TransferId = Guid.NewGuid().ToString("N");

        var fileInfo = new FileInfo(filePath);
        this.FileName = fileInfo.Name;
        this.FileSize = fileInfo.Length;
        this.TotalChunks = (int)Math.Ceiling((double)fileInfo.Length / ChunkSize);

        // Subscribe to connection events
        this._connection.FileSendResponseReceived += this.OnFileSendResponseReceived;
        this._connection.FileCancelReceived += this.OnFileCancelReceived;
        this._connection.FileErrorReceived += this.OnFileErrorReceived;
    }

    public string TransferId { get; }
    public string FilePath { get; }
    public string FileName { get; }
    public long FileSize { get; }
    public int TotalChunks { get; }

    [ObservableProperty]
    private int _currentChunk;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private OutgoingTransferState _state = OutgoingTransferState.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    public string FileSizeFormatted => FormatFileSize(this.FileSize);
    public int ProgressPercent => (int)(this.Progress * 100);

    public event EventHandler? Completed;
    public event EventHandler? Failed;

    public async Task StartAsync()
    {
        if (this.State != OutgoingTransferState.Pending)
            return;

        this.State = OutgoingTransferState.WaitingForAcceptance;
        await this._connection.SendFileSendRequestAsync(this.TransferId, this.FileName, this.FileSize);
    }

    public async Task CancelAsync()
    {
        if (this.State is OutgoingTransferState.Completed or OutgoingTransferState.Failed or OutgoingTransferState.Cancelled)
            return;

        this.State = OutgoingTransferState.Cancelled;
        await this._connection.SendFileCancelAsync(this.TransferId, "Cancelled by user");
        this.Cleanup();
    }

    private void OnFileSendResponseReceived(object? sender, FileSendResponseReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        if (e.Accepted)
        {
            this.State = OutgoingTransferState.Transferring;
            _ = Task.Run(this.SendChunksAsync);
        }
        else
        {
            this.State = OutgoingTransferState.Failed;
            this.ErrorMessage = e.ErrorMessage ?? "Transfer rejected by presenter";
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnFileCancelReceived(object? sender, FileCancelReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = OutgoingTransferState.Cancelled;
        this.ErrorMessage = e.Reason;
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFileErrorReceived(object? sender, FileErrorReceivedEventArgs e)
    {
        if (e.TransferId != this.TransferId)
            return;

        this.State = OutgoingTransferState.Failed;
        this.ErrorMessage = e.ErrorMessage;
        this.Cleanup();
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SendChunksAsync()
    {
        try
        {
            this._fileStream = new FileStream(this.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[ChunkSize];

            while (this.State == OutgoingTransferState.Transferring)
            {
                var bytesRead = await this._fileStream.ReadAsync(buffer);
                if (bytesRead == 0)
                    break;

                var chunk = new FileChunkMessage(
                    this.TransferId,
                    this.CurrentChunk,
                    this.TotalChunks,
                    buffer.AsMemory(0, bytesRead));

                await this._connection.SendFileChunkAsync(chunk);

                this.CurrentChunk++;
                this.Progress = (double)this.CurrentChunk / this.TotalChunks;

                // Small yield to avoid overwhelming the connection
                await Task.Delay(1);
            }

            if (this.State == OutgoingTransferState.Transferring)
            {
                await this._connection.SendFileCompleteAsync(this.TransferId);
                this.State = OutgoingTransferState.Completed;
                this.Progress = 1.0;
                this.Cleanup();
                this.Completed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            this.State = OutgoingTransferState.Failed;
            this.ErrorMessage = ex.Message;
            await this._connection.SendFileErrorAsync(this.TransferId, ex.Message);
            this.Cleanup();
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Cleanup()
    {
        this._fileStream?.Dispose();
        this._fileStream = null;

        // Unsubscribe from events
        this._connection.FileSendResponseReceived -= this.OnFileSendResponseReceived;
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

public enum OutgoingTransferState
{
    Pending,
    WaitingForAcceptance,
    Transferring,
    Completed,
    Failed,
    Cancelled
}

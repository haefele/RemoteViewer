using System.Security.Cryptography;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Clipboard;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class ClipboardSyncService : IClipboardSyncServiceImpl, IDisposable
{
    private const int MaxClipboardSize = 1 * 1024 * 1024; // 1MB
    private const int PollIntervalMs = 500;

    private readonly IClipboardService _clipboardService;
    private readonly Connection _connection;
    private readonly ILogger<ClipboardSyncService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _stateLock = new();
    private Task? _pollTask;

    private byte[] _lastSentHash = [];
    private byte[] _lastReceivedHash = [];

    private int _disposed;

    public ClipboardSyncService(
        IClipboardService clipboardService,
        Connection connection,
        ILogger<ClipboardSyncService> logger)
    {
        this._clipboardService = clipboardService;
        this._connection = connection;
        this._logger = logger;

        this._pollTask = Task.Run(() => this.PollLoopAsync(this._cts.Token));
        this._logger.ClipboardSyncStarted();
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (this._disposed == 1 || this._connection.IsClosed)
                break;

            try
            {
                await this.CheckAndSendClipboardAsync();
            }
            catch (Exception ex)
            {
                this._logger.ErrorCheckingClipboard(ex);
            }
        }
    }

    private async Task CheckAndSendClipboardAsync()
    {
        if (this._connection.IsClosed)
            return;

        // Check for text first
        var text = await this._clipboardService.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text) && text.Length <= MaxClipboardSize)
        {
            if (this.TryMarkAsChanged(ComputeHash(text)))
            {
                await this.SendAsync(MessageTypes.Clipboard.Text, new ClipboardTextMessage(text));
                this._logger.SentClipboardText(text.Length);
            }
            return;
        }

        // Skip files - user should use file transfer
        var formats = await this._clipboardService.GetDataFormatsAsync();
        if (formats.Contains(DataFormat.File))
            return;

        // Try to get image data
        var imageData = await this.TryGetClipboardImageAsync(formats);
        if (imageData is { } data && data.Length <= MaxClipboardSize)
        {
            if (this.TryMarkAsChanged(ComputeHash(data)))
            {
                await this.SendAsync(MessageTypes.Clipboard.Image, new ClipboardImageMessage(data), data.Length + 256);
                this._logger.SentClipboardImage(data.Length);
            }
        }
    }

    private async Task<byte[]?> TryGetClipboardImageAsync(IReadOnlyList<DataFormat> formats)
    {
        try
        {
            if (formats.Contains(DataFormat.Bitmap))
            {
                var bitmap = await this._clipboardService.TryGetBitmapAsync();
                if (bitmap is not null)
                {
                    using var ms = new MemoryStream();
                    bitmap.Save(ms);

                    return ms.ToArray();
                }
            }
        }
        catch (Exception ex)
        {
            this._logger.FailedToGetClipboardImage(ex);
        }

        return null;
    }

    private bool TryMarkAsChanged(byte[] hash)
    {
        using (this._stateLock.EnterScope())
        {
            if (hash.SequenceEqual(this._lastSentHash) || hash.SequenceEqual(this._lastReceivedHash))
                return false;

            this._lastSentHash = hash;
            return true;
        }
    }

    private async Task SendAsync<T>(string messageType, T message, int bufferSize = 256)
    {
        if (this._connection.IsClosed)
            return;

        using var buffer = PooledBufferWriter.Rent(bufferSize);
        ProtocolSerializer.Serialize(buffer, message);

        var destination = this._connection.IsPresenter
            ? MessageDestination.AllViewers
            : MessageDestination.PresenterOnly;

        await this._connection.Owner.SendMessageAsync(
            this._connection.ConnectionId,
            messageType,
            buffer.WrittenMemory,
            destination,
            null);
    }

    async Task IClipboardSyncServiceImpl.HandleTextMessageAsync(ClipboardTextMessage message)
    {
        using (this._stateLock.EnterScope())
        {
            this._lastReceivedHash = ComputeHash(message.Text);
        }

        try
        {
            await this._clipboardService.SetTextAsync(message.Text);
            this._logger.ReceivedClipboardText(message.Text.Length);
        }
        catch (Exception ex)
        {
            this._logger.FailedToSetClipboardText(ex);
        }
    }

    async Task IClipboardSyncServiceImpl.HandleImageMessageAsync(ClipboardImageMessage message)
    {
        using (this._stateLock.EnterScope())
        {
            this._lastReceivedHash = ComputeHash(message.Data.Span);
        }

        try
        {
            using var stream = new MemoryStream();
            stream.Write(message.Data.Span);
            stream.Position = 0;

            var item = new DataTransferItem();
            item.SetBitmap(new Bitmap(stream));

            var dataTransfer = new DataTransfer();
            dataTransfer.Add(item);
            await this._clipboardService.SetDataAsync(dataTransfer);

            this._logger.ReceivedClipboardImage(message.Data.Length);
        }
        catch (Exception ex)
        {
            this._logger.FailedToSetClipboardImage(ex);
        }
    }

    private static byte[] ComputeHash(string text)
        => SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text));

    private static byte[] ComputeHash(byte[] data)
        => SHA256.HashData(data);

    private static byte[] ComputeHash(ReadOnlySpan<byte> data)
        => SHA256.HashData(data);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

        this._cts.Cancel();
        this._pollTask?.Wait();
        this._cts.Dispose();

        this._logger.ClipboardSyncStopped();
    }
}

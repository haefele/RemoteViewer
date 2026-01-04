using System.Security.Cryptography;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

public sealed class ClipboardSyncService : IClipboardSyncServiceImpl, IDisposable
{
    private const int MaxClipboardSize = 1 * 1024 * 1024; // 1MB
    private const int PollIntervalMs = 500;

    private readonly App _app;
    private readonly Connection _connection;
    private readonly ILogger<ClipboardSyncService> _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly Lock _stateLock = new();
    private Task? _pollTask;

    private byte[] _lastSentHash = [];
    private byte[] _lastReceivedHash = [];

    private int _disposed;

    public ClipboardSyncService(
        App app,
        Connection connection,
        ILogger<ClipboardSyncService> logger)
    {
        this._app = app;
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

        // Must access clipboard on UI thread
        var clipboardData = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = this.GetClipboard();
            if (clipboard is null)
                return (Text: null, ImageData: null);

            // Check for text first
            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text) && text.Length <= MaxClipboardSize)
                return (Text: text, ImageData: null);

            // Skip files - user should use file transfer
            var formats = await clipboard.GetDataFormatsAsync();
            if (formats.Contains(DataFormat.File))
                return (Text: null, ImageData: null);

            // Try to get image data
            var imageData = await this.TryGetClipboardImageAsync(clipboard);
            return (Text: (string?)null, ImageData: imageData);
        });

        if (clipboardData.Text is { } text)
        {
            if (this.TryMarkAsChanged(ComputeHash(text)))
            {
                await this.SendAsync(MessageTypes.Clipboard.Text, new ClipboardTextMessage(text));
                this._logger.SentClipboardText(text.Length);
            }
        }
        else if (clipboardData.ImageData is { } data && data.Length <= MaxClipboardSize)
        {
            if (this.TryMarkAsChanged(ComputeHash(data)))
            {
                await this.SendAsync(MessageTypes.Clipboard.Image, new ClipboardImageMessage(data), data.Length + 256);
                this._logger.SentClipboardImage(data.Length);
            }
        }
    }

    private async Task<byte[]?> TryGetClipboardImageAsync(IClipboard clipboard)
    {
        try
        {
            var formats = await clipboard.GetDataFormatsAsync();
            if (formats.Contains(DataFormat.Bitmap))
            {
                var bitmap = await clipboard.TryGetBitmapAsync();
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

    void IClipboardSyncServiceImpl.HandleTextMessage(ClipboardTextMessage message)
    {
        this.HandleIncoming(
            ComputeHash(message.Text),
            async clipboard =>
            {
                await clipboard.SetTextAsync(message.Text);
                this._logger.ReceivedClipboardText(message.Text.Length);
            },
            ex => this._logger.FailedToSetClipboardText(ex));
    }

    void IClipboardSyncServiceImpl.HandleImageMessage(ClipboardImageMessage message)
    {
        this.HandleIncoming(
            ComputeHash(message.Data.Span),
            async clipboard =>
            {
                using var stream = new MemoryStream();
                stream.Write(message.Data.Span);

                var item = new DataTransferItem();
                item.SetBitmap(new Bitmap(stream));

                var dataTransfer = new DataTransfer();
                dataTransfer.Add(item);
                await clipboard.SetDataAsync(dataTransfer);

                this._logger.ReceivedClipboardImage(message.Data.Length);
            },
            ex => this._logger.FailedToSetClipboardImage(ex));
    }

    private void HandleIncoming(byte[] hash, Func<IClipboard, Task> setClipboard, Action<Exception> onError)
    {
        using (this._stateLock.EnterScope())
        {
            this._lastReceivedHash = hash;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var clipboard = this.GetClipboard();
                if (clipboard is not null)
                    await setClipboard(clipboard);
            }
            catch (Exception ex)
            {
                onError(ex);
            }
        });
    }

    private IClipboard? GetClipboard()
    {
        try
        {
            return this._app.ActiveWindow?.Clipboard;
        }
        catch
        {
            return null;
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

using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace RemoteViewer.Client.Services.Clipboard;

public sealed class AvaloniaClipboardService(App app) : IClipboardService
{
    public Task<string?> TryGetTextAsync()
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = app.ActiveWindow?.Clipboard;
            return clipboard is not null ? await clipboard.TryGetTextAsync() : null;
        });

    public Task<IReadOnlyList<DataFormat>> GetDataFormatsAsync()
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = app.ActiveWindow?.Clipboard;
            return clipboard is not null ? await clipboard.GetDataFormatsAsync() : (IReadOnlyList<DataFormat>)[];
        });

    public Task<Bitmap?> TryGetBitmapAsync()
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = app.ActiveWindow?.Clipboard;
            return clipboard is not null ? await clipboard.TryGetBitmapAsync() : null;
        });

    public Task SetTextAsync(string text)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = app.ActiveWindow?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(text);
        });

    public Task SetDataAsync(IAsyncDataTransfer? data)
        => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var clipboard = app.ActiveWindow?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetDataAsync(data);
        });
}

using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace RemoteViewer.Client.Services.Clipboard;

public interface IClipboardService
{
    Task<string?> TryGetTextAsync();
    Task<IReadOnlyList<DataFormat>> GetDataFormatsAsync();
    Task<Bitmap?> TryGetBitmapAsync();
    Task SetTextAsync(string text);
    Task SetDataAsync(IAsyncDataTransfer? data);
}

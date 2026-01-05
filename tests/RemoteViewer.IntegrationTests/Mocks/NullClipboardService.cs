using Avalonia.Input;
using Avalonia.Media.Imaging;
using RemoteViewer.Client.Services.Clipboard;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullClipboardService : IClipboardService
{
    public Task<string?> TryGetTextAsync() => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<DataFormat>> GetDataFormatsAsync() => Task.FromResult<IReadOnlyList<DataFormat>>([]);
    public Task<Bitmap?> TryGetBitmapAsync() => Task.FromResult<Bitmap?>(null);
    public Task SetTextAsync(string text) => Task.CompletedTask;
    public Task SetDataAsync(IAsyncDataTransfer? data) => Task.CompletedTask;
}

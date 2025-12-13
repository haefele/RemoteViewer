using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class ToastItemViewModel : ObservableObject, IDisposable
{
    private readonly Action<ToastItemViewModel> _removeCallback;
    private readonly CancellationTokenSource _dismissCts = new();
    private bool _disposed;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private ToastType _type;

    public ToastItemViewModel(string message, ToastType type, int durationMs, Action<ToastItemViewModel> removeCallback)
    {
        this._message = message;
        this._type = type;
        this._removeCallback = removeCallback;

        this.StartDismissTimer(durationMs);
    }

    private async void StartDismissTimer(int durationMs)
    {
        try
        {
            await Task.Delay(durationMs, this._dismissCts.Token);
            await Dispatcher.UIThread.InvokeAsync(() => this._removeCallback(this));
        }
        catch (TaskCanceledException)
        {
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        this._dismissCts.Cancel();
        this._removeCallback(this);
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._dismissCts.Cancel();
        this._dismissCts.Dispose();

        GC.SuppressFinalize(this);
    }
}

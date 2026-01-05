using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using RemoteViewer.Client.Services.Dispatching;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class ActionToastItemViewModel : ObservableObject, IDisposable
{
    private readonly IDispatcher _dispatcher;
    private readonly Action<ActionToastItemViewModel> _removeCallback;
    private readonly Action _actionCallback;
    private readonly CancellationTokenSource _dismissCts = new();
    private bool _disposed;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private ToastType _type;

    [ObservableProperty]
    private string _actionText;

    [ObservableProperty]
    private MaterialIconKind _actionIcon;

    public ActionToastItemViewModel(
        string message,
        ToastType type,
        string actionText,
        MaterialIconKind actionIcon,
        Action actionCallback,
        int durationMs,
        IDispatcher dispatcher,
        Action<ActionToastItemViewModel> removeCallback)
    {
        this._message = message;
        this._type = type;
        this._actionText = actionText;
        this._actionIcon = actionIcon;
        this._actionCallback = actionCallback;
        this._dispatcher = dispatcher;
        this._removeCallback = removeCallback;

        this.StartDismissTimer(durationMs);
    }

    private async void StartDismissTimer(int durationMs)
    {
        try
        {
            await Task.Delay(durationMs, this._dismissCts.Token);
            await this._dispatcher.InvokeAsync(() => this._removeCallback(this));
        }
        catch (TaskCanceledException)
        {
        }
    }

    [RelayCommand]
    private void ExecuteAction()
    {
        this._actionCallback();
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

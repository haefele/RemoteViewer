using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class ToastsViewModel : ObservableObject
{
    public ObservableCollection<ToastItemViewModel> Items { get; } = new();

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastItemViewModel(message, type, durationMs, this.RemoveToast);
            this.Items.Add(toast);
        });
    }

    public void Success(string message, int durationMs = 3000) =>
        this.Show(message, ToastType.Success, durationMs);

    public void Error(string message, int durationMs = 4000) =>
        this.Show(message, ToastType.Error, durationMs);

    public void Info(string message, int durationMs = 3000) =>
        this.Show(message, ToastType.Info, durationMs);

    private void RemoveToast(ToastItemViewModel toast)
    {
        Dispatcher.UIThread.Post(() => this.Items.Remove(toast));
    }
}

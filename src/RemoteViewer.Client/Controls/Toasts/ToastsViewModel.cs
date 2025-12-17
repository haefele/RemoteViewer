using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RemoteViewer.Client.Services.FileTransfer;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class ToastsViewModel : ObservableObject
{
    public ObservableCollection<object> Items { get; } = new();

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

    public TransferToastItemViewModel AddTransfer(IFileTransfer transfer, bool isUpload)
    {
        var toast = new TransferToastItemViewModel(transfer, isUpload, this.OnTransferCompleted);
        Dispatcher.UIThread.Post(() => this.Items.Add(toast));
        return toast;
    }

    private void OnTransferCompleted(TransferToastItemViewModel toast, bool success, string? error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            this.Items.Remove(toast);
            toast.Dispose();

            var fileName = toast.Transfer.FileName ?? "File";
            if (success)
            {
                this.Success($"File {(toast.IsUpload ? "sent" : "received")}: {fileName}");
            }
            else
            {
                this.Error($"File {(toast.IsUpload ? "upload" : "download")} failed: {error ?? "Unknown error"}");
            }
        });
    }

    private void RemoveToast(ToastItemViewModel toast)
    {
        Dispatcher.UIThread.Post(() => this.Items.Remove(toast));
    }
}

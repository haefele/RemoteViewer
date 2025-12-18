using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Material.Icons;
using RemoteViewer.Client.Services.FileTransfer;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class ToastsViewModel : ObservableObject
{
    public ObservableCollection<object> Items { get; } = new();

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 4000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ToastItemViewModel(message, type, durationMs, this.RemoveToast);
            this.Items.Add(toast);
        });
    }

    public void Success(string message, int durationMs = 4000) =>
        this.Show(message, ToastType.Success, durationMs);

    public void Error(string message, int durationMs = 4000) =>
        this.Show(message, ToastType.Error, durationMs);

    public void Info(string message, int durationMs = 4000) =>
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
                if (toast.IsUpload)
                {
                    this.Success($"File sent: {fileName}");
                }
                else
                {
                    this.SuccessWithAction(
                        $"File received: {fileName}",
                        "Open folder",
                        MaterialIconKind.FolderOpen,
                        OpenDownloadsFolder);
                }
            }
            else
            {
                this.Error(error ?? "An unknown error occurred.");
            }
        });
    }

    public void SuccessWithAction(string message, string actionText, MaterialIconKind actionIcon, Action action, int durationMs = 4000)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var toast = new ActionToastItemViewModel(message, ToastType.Success, actionText, actionIcon, action, durationMs, this.RemoveToast);
            this.Items.Add(toast);
        });
    }

    private static void OpenDownloadsFolder()
    {
        var downloadsPath = FileTransferHelpers.GetDownloadsFolder();
        Process.Start(new ProcessStartInfo
        {
            FileName = downloadsPath,
            UseShellExecute = true
        });
    }

    private void RemoveToast(object toast)
    {
        Dispatcher.UIThread.Post(() => this.Items.Remove(toast));
    }
}

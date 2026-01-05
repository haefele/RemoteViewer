using Avalonia.Threading;
using RemoteViewer.Client.Controls.Dialogs;

namespace RemoteViewer.Client.Services.Dialogs;

public sealed class AvaloniaDialogService(App app) : IDialogService
{
    public Task<bool> ShowFileTransferConfirmationAsync(string senderDisplayName, string fileName, string fileSizeFormatted)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = FileTransferConfirmationDialog.Create(senderDisplayName, fileName, fileSizeFormatted);
            return await dialog.ShowDialog<bool?>(app.ActiveWindow) ?? false;
        });
    }
}

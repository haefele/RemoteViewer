using Avalonia.Threading;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Views.Presenter;

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

    public Task<IReadOnlyList<string>?> ShowViewerSelectionAsync(IReadOnlyList<PresenterViewerDisplay> viewers, string fileName, string fileSizeFormatted)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = ViewerSelectionDialog.Create(viewers, fileName, fileSizeFormatted);
            return await dialog.ShowDialog<IReadOnlyList<string>?>(app.ActiveWindow);
        });
    }
}

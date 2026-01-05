using RemoteViewer.Client.Views.Presenter;

namespace RemoteViewer.Client.Services.Dialogs;

public interface IDialogService
{
    Task<bool> ShowFileTransferConfirmationAsync(string senderDisplayName, string fileName, string fileSizeFormatted);

    Task<IReadOnlyList<string>?> ShowViewerSelectionAsync(
        IReadOnlyList<PresenterViewerDisplay> viewers,
        string fileName,
        string fileSizeFormatted);
}

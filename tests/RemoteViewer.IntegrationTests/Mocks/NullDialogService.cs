using RemoteViewer.Client.Services.Dialogs;
using RemoteViewer.Client.Views.Presenter;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullDialogService : IDialogService
{
    public Task<bool> ShowFileTransferConfirmationAsync(string senderDisplayName, string fileName, string fileSizeFormatted)
        => Task.FromResult(false);

    public Task<IReadOnlyList<string>?> ShowViewerSelectionAsync(
        IReadOnlyList<PresenterViewerDisplay> viewers,
        string fileName,
        string fileSizeFormatted)
        => Task.FromResult<IReadOnlyList<string>?>(null);
}

using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;

namespace RemoteViewer.Client.Services.Dialogs;

public interface IDialogService
{
    Task<bool> ShowFileTransferConfirmationAsync(FileTransferConfirmationDialogViewModel viewModel);

    Task<IReadOnlyList<string>?> ShowViewerSelectionAsync(ViewerSelectionDialogViewModel viewModel);

    IWindowHandle ShowPresenterWindow(PresenterViewModel viewModel);

    IWindowHandle ShowViewerWindow(ViewerViewModel viewModel);

    Task ShowAboutDialogAsync();

    IWindowHandle ShowChatWindow(ChatViewModel viewModel);
}

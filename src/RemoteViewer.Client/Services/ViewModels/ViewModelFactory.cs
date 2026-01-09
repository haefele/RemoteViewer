using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Views.About;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Views.Main;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;

namespace RemoteViewer.Client.Services.ViewModels;

public interface IViewModelFactory
{
    MainViewModel CreateMainViewModel();

    PresenterViewModel CreatePresenterViewModel(Connection connection);
    ViewerViewModel CreateViewerViewModel(Connection connection);

    ToastsViewModel CreateToastsViewModel();
    AboutViewModel CreateAboutViewModel();
    ChatViewModel CreateChatViewModel(ChatService chatService);
    FileTransferConfirmationDialogViewModel CreateFileTransferConfirmationDialogViewModel(string senderDisplayName, string fileName, string fileSizeFormatted);
    ViewerSelectionDialogViewModel CreateViewerSelectionDialogViewModel(IReadOnlyList<PresenterViewerDisplay> viewers, string fileName, string fileSizeFormatted);
}

public class ViewModelFactory(IServiceProvider serviceProvider) : IViewModelFactory
{
    public MainViewModel CreateMainViewModel() => ActivatorUtilities.CreateInstance<MainViewModel>(serviceProvider);
    public PresenterViewModel CreatePresenterViewModel(Connection connection) => ActivatorUtilities.CreateInstance<PresenterViewModel>(serviceProvider, connection);
    public ViewerViewModel CreateViewerViewModel(Connection connection) => ActivatorUtilities.CreateInstance<ViewerViewModel>(serviceProvider, connection);

    public ToastsViewModel CreateToastsViewModel() => ActivatorUtilities.CreateInstance<ToastsViewModel>(serviceProvider);
    public AboutViewModel CreateAboutViewModel() => ActivatorUtilities.CreateInstance<AboutViewModel>(serviceProvider);
    public ChatViewModel CreateChatViewModel(ChatService chatService) => ActivatorUtilities.CreateInstance<ChatViewModel>(serviceProvider, chatService);
    public FileTransferConfirmationDialogViewModel CreateFileTransferConfirmationDialogViewModel(string senderDisplayName, string fileName, string fileSizeFormatted) =>
        new(senderDisplayName, fileName, fileSizeFormatted);
    public ViewerSelectionDialogViewModel CreateViewerSelectionDialogViewModel(IReadOnlyList<PresenterViewerDisplay> viewers, string fileName, string fileSizeFormatted) =>
        new(viewers, fileName, fileSizeFormatted);
}

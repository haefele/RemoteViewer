using Avalonia.Controls;
using Avalonia.Threading;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.About;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;

namespace RemoteViewer.Client.Services.Dialogs;

public sealed class AvaloniaDialogService : IDialogService
{
    private readonly App _app;
    private readonly IViewModelFactory _viewModelFactory;

    public AvaloniaDialogService(App app, IViewModelFactory viewModelFactory)
    {
        this._app = app;
        this._viewModelFactory = viewModelFactory;
    }

    public Task<bool> ShowFileTransferConfirmationAsync(FileTransferConfirmationDialogViewModel viewModel)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new FileTransferConfirmationDialog
            {
                DataContext = viewModel
            };
            return await dialog.ShowDialog<bool?>(this._app.ActiveWindow) ?? false;
        });
    }

    public Task<IReadOnlyList<string>?> ShowViewerSelectionAsync(ViewerSelectionDialogViewModel viewModel)
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new ViewerSelectionDialog
            {
                DataContext = viewModel
            };
            return await dialog.ShowDialog<IReadOnlyList<string>?>(this._app.ActiveWindow);
        });
    }

    public IWindowHandle ShowPresenterWindow(PresenterViewModel viewModel)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var window = new PresenterView
            {
                DataContext = viewModel
            };
            window.Show();
            return new WindowHandle(window);
        });
    }

    public IWindowHandle ShowViewerWindow(ViewerViewModel viewModel)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ViewerView
            {
                DataContext = viewModel
            };
            window.Show();
            return new WindowHandle(window);
        });
    }

    public Task ShowAboutDialogAsync()
    {
        return Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var viewModel = this._viewModelFactory.CreateAboutViewModel();
            var dialog = new AboutView { DataContext = viewModel };
            await dialog.ShowDialog(this._app.ActiveWindow);
        });
    }

    public IWindowHandle ShowChatWindow(ChatViewModel viewModel)
    {
        return Dispatcher.UIThread.Invoke(() =>
        {
            var window = new ChatView
            {
                DataContext = viewModel
            };
            window.Show();
            window.Activate();
            return new WindowHandle(window);
        });
    }
}

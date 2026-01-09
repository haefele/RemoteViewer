using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Views.About;
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
}

public class ViewModelFactory(IServiceProvider serviceProvider) : IViewModelFactory
{
    public MainViewModel CreateMainViewModel() => ActivatorUtilities.CreateInstance<MainViewModel>(serviceProvider);
    public PresenterViewModel CreatePresenterViewModel(Connection connection) => ActivatorUtilities.CreateInstance<PresenterViewModel>(serviceProvider, connection);
    public ViewerViewModel CreateViewerViewModel(Connection connection) => ActivatorUtilities.CreateInstance<ViewerViewModel>(serviceProvider, connection);

    public ToastsViewModel CreateToastsViewModel() => ActivatorUtilities.CreateInstance<ToastsViewModel>(serviceProvider);
    public AboutViewModel CreateAboutViewModel() => ActivatorUtilities.CreateInstance<AboutViewModel>(serviceProvider);
}

using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client.Views.Main;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;

namespace RemoteViewer.Client.Services;

public interface IViewModelFactory
{
    MainViewModel CreateMainViewModel();

    PresenterViewModel CreatePresenterViewModel(Connection connection);
    ViewerViewModel CreateViewerViewModel(Connection connection);
}

public class ViewModelFactory(IServiceProvider serviceProvider) : IViewModelFactory
{
    public MainViewModel CreateMainViewModel() => ActivatorUtilities.CreateInstance<MainViewModel>(serviceProvider);
    public PresenterViewModel CreatePresenterViewModel(Connection connection) => ActivatorUtilities.CreateInstance<PresenterViewModel>(serviceProvider, connection);
    public ViewerViewModel CreateViewerViewModel(Connection connection) => ActivatorUtilities.CreateInstance<ViewerViewModel>(serviceProvider, connection);
}

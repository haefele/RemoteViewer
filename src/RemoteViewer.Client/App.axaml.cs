using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Views.Main;
using Serilog;
#if WINDOWS
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.WinServ.Services;
#endif

namespace RemoteViewer.Client;

public partial class App : Application
{
    private const string ServerUrl = "http://100.123.102.66:5000";

    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // Build DI container
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddSerilog());

            services.AddSingleton(sp =>
                new ConnectionHubClient(ServerUrl, sp.GetRequiredService<ILogger<ConnectionHubClient>>()));

#if WINDOWS
            // Screen capture services from WinServ
            services.AddSingleton<DxgiScreenGrabber>();
            services.AddSingleton<BitBltScreenGrabber>();
            services.AddSingleton<IScreenshotService, ScreenshotService>();
            services.AddSingleton<InputInjectionService>();
            services.AddSingleton<PresenterService>();
#endif

            services.AddTransient<MainViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            // Resolve view model from DI
            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();

            var mainView = new MainView
            {
                DataContext = viewModel,
            };

            // Wire up MainView visibility events
            viewModel.RequestHideMainView += (_, _) => mainView.Hide();
            viewModel.RequestShowMainView += (_, _) => mainView.Show();

            desktop.MainWindow = mainView;

            mainView.Opened += async (_, _) =>
            {
#if WINDOWS
                // Resolve PresenterService to start listening for presenter connections
                _ = _serviceProvider.GetRequiredService<PresenterService>();
#endif
                await viewModel.InitializeAsync();
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

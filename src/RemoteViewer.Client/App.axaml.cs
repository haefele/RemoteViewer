using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.ViewModels;
using RemoteViewer.Client.Views;
using Serilog;

namespace RemoteViewer.Client;

public partial class App : Application
{
    private const string ServerUrl = "http://localhost:5000";

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

            services.AddTransient<MainWindowViewModel>();

            _serviceProvider = services.BuildServiceProvider();

            // Resolve view model from DI
            var viewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.MainWindow = mainWindow;

            mainWindow.Opened += async (_, _) =>
            {
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

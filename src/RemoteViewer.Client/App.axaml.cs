using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.Main;

namespace RemoteViewer.Client;

public partial class App : Application
{
    public static new App Current => Application.Current as App ?? throw new InvalidOperationException("Current application is not of type App");

    private IServiceProvider? _serviceProvider;
    public IServiceProvider Services => this._serviceProvider ?? throw new InvalidOperationException("Services not initialized");

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            this._serviceProvider = BuildServiceProvider(this);

            desktop.MainWindow = new MainView
            {
                DataContext = this._serviceProvider
                    .GetRequiredService<IViewModelFactory>()
                    .CreateMainViewModel(),
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                if (this._serviceProvider is IDisposable disposable)
                    disposable.Dispose();
            };

            // Connect to the hub in the background, don't wait for it
            _ = this._serviceProvider.GetRequiredService<ConnectionHubClient>().ConnectToHub();
        }
    }

    private static IServiceProvider BuildServiceProvider(App app)
    {
        var services = new ServiceCollection();

        services.AddDesktopServices(app);

        return services.BuildServiceProvider();
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

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
using RemoteViewer.Client.Services.Windows;
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
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // Build DI container
            var services = new ServiceCollection();

            services.AddLogging(builder => builder.AddSerilog());

            services.AddSingleton(sp =>
                new ConnectionHubClient(ServerUrl, sp.GetRequiredService<ILogger<ConnectionHubClient>>()));

            // Platform-specific services
            if (OperatingSystem.IsWindows())
            {
                RegisterWindowsServices(services);
            }
            else
            {
                RegisterNullServices(services);
            }

            services.AddTransient<MainViewModel>();

            this._serviceProvider = services.BuildServiceProvider();

            // Resolve view model from DI
            var viewModel = this._serviceProvider.GetRequiredService<MainViewModel>();

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
                await viewModel.InitializeAsync();
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                this._serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

#if WINDOWS
    private static void RegisterWindowsServices(IServiceCollection services)
    {
        services.AddSingleton<DxgiScreenGrabber>();
        services.AddSingleton<BitBltScreenGrabber>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IInputInjectionService, InputInjectionService>();
    }
#else
    private static void RegisterWindowsServices(IServiceCollection services)
    {
        // This path should never be hit at runtime on non-Windows,
        // but the method needs to exist for compilation
        RegisterNullServices(services);
    }
#endif

    private static void RegisterNullServices(IServiceCollection services)
    {
        services.AddSingleton<IScreenshotService, NullScreenshotService>();
        services.AddSingleton<IInputInjectionService, NullInputInjectionService>();
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

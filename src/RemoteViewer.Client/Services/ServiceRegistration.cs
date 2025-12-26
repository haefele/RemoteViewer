using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Services.WindowsIpc;
using RemoteViewer.Client.Services.WindowsSession;
using Serilog;

namespace RemoteViewer.Client.Services;

static class ServiceRegistration
{
    public static IServiceCollection AddDesktopServices(this IServiceCollection services, App app)
    {
        services.AddCoreServices();
        services.AddCaptureServices();

        services.AddSingleton(app);
        services.AddSingleton<ConnectionHubClient>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();
        services.AddSingleton<ScreenEncoder>();
        services.AddSingleton<SessionRecorderRpcClient>();
        services.AddSingleton<ILocalInputMonitorService, WindowsLocalInputMonitorService>();
        services.AddSingleton<IScreenGrabber, IpcScreenGrabber>();
        services.AddSingleton<IDisplayService, WindowsHybridDisplayService>();
        services.AddSingleton<IInputInjectionService, WindowsHybridInputInjectionService>();
        return services;
    }

    public static IServiceCollection AddHostedModeServices(this IServiceCollection services, ApplicationMode mode)
    {
        services.AddCoreServices();

        services.AddSingleton<IWin32SessionService, Win32SessionService>();

        if (mode is ApplicationMode.SessionRecorder)
        {
            services.AddCaptureServices();
            services.AddSingleton<SessionRecorderRpcServer>();
            services.AddHostedService<SessionRecorderRpcHostService>();
        }
        else
        {
            services.AddHostedService<TrackActiveSessionsBackgroundService>();
            services.AddWindowsService();
        }

        return services;
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddFusionCache();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSerilog();

        return services;
    }

    private static IServiceCollection AddCaptureServices(this IServiceCollection services)
    {
        services.AddSingleton<IScreenGrabber, DxgiScreenGrabber>();
        services.AddSingleton<IScreenGrabber, BitBltScreenGrabber>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();

        services.AddSingleton<WindowsDisplayService>();
        services.AddSingleton<WindowsInputInjectionService>();

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
using ZiggyCreatures.Caching.Fusion;

namespace RemoteViewer.Client.Services;

static class ServiceRegistration
{
    public static IServiceCollection AddRemoteViewerServices(this IServiceCollection services, ApplicationMode mode, App? app = null) => mode switch
    {
        ApplicationMode.Desktop => services.AddDesktopModeServices(app!),
        ApplicationMode.WindowsService => services.AddWindowsServiceModeServices(),
        ApplicationMode.SessionRecorder => services.AddSessionRecorderModeServices(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    private static IServiceCollection AddDesktopModeServices(this IServiceCollection services, App app)
    {
        // Core Services
        services.AddCoreServices();

        // UI & View Models
        services.AddSingleton(app);
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();

        // Hub Connection
        services.AddSingleton<ConnectionHubClient>();

        // Screen Encoding
        services.AddSingleton<IFrameEncoder, TurboJpegFrameEncoder>();

        // IPC Client (to communicate with SessionRecorder)
        services.AddSingleton<SessionRecorderRpcClient>();

        // Screen Capture (IPC-first, with local fallback)
        services.AddSingleton<IScreenGrabber, IpcScreenGrabber>();
        services.AddScreenCaptureServices();

        // Display & Input (with IPC fallback via injected SessionRecorderRpcClient)
        services.AddSingleton<IDisplayService, WindowsDisplayService>();
        services.AddSingleton<IInputInjectionService, WindowsInputInjectionService>();

        // Local Input Monitoring
        services.AddSingleton<ILocalInputMonitorService, WindowsLocalInputMonitorService>();

        return services;
    }


    private static IServiceCollection AddWindowsServiceModeServices(this IServiceCollection services)
    {
        // Core Services
        services.AddCoreServices();

        // Session Management
        services.AddSingleton<IWin32SessionService, Win32SessionService>();

        // Background Service (spawns SessionRecorder processes)
        services.AddHostedService<TrackActiveSessionsBackgroundService>();

        // Windows Service Integration
        services.AddWindowsService();

        return services;
    }


    private static IServiceCollection AddSessionRecorderModeServices(this IServiceCollection services)
    {
        // Core Services
        services.AddCoreServices();

        // Session Management
        services.AddSingleton<IWin32SessionService, Win32SessionService>();

        // Screen Capture (local only, no IPC)
        services.AddScreenCaptureServices();

        // Display & Input (local only, no IPC fallback)
        services.AddSingleton<IDisplayService>(sp => new WindowsDisplayService(
            null,
            sp.GetRequiredService<IFusionCache>(),
            sp.GetRequiredService<ILogger<WindowsDisplayService>>()));
        services.AddSingleton<IInputInjectionService>(sp => new WindowsInputInjectionService(
            null,
            sp.GetRequiredService<ILogger<WindowsInputInjectionService>>()));

        // IPC Server (serves Desktop client requests)
        services.AddSingleton<SessionRecorderRpcServer>();
        services.AddHostedService<SessionRecorderRpcHostService>();

        return services;
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddFusionCache();
        services.AddLogging(builder => builder.AddSerilog());
        services.AddSerilog();
        return services;
    }

    private static IServiceCollection AddScreenCaptureServices(this IServiceCollection services)
    {
        services.AddSingleton<IScreenGrabber, DxgiScreenGrabber>();
        services.AddSingleton<IScreenGrabber, BitBltScreenGrabber>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        return services;
    }
}

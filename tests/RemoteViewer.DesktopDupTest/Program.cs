using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using NLog.Extensions.Hosting;
using NLog.Extensions.Logging;
using RemoteViewer.DesktopDupTest;

namespace RemoteViewer.DesktopDupTest;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = CreateHostBuilder(args).Build();
        await host.RunAsync();
    }

    private static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .ConfigureServices((context, services) =>
        {
            services.AddOptions<RemoteViewerOptions>()
                    .Bind(context.Configuration.GetSection("RemoteViewer"))
                    .PostConfigure(options =>
                    {
                        if (options.Mode is RemoteViewerMode.Undefined && WindowsServiceHelpers.IsWindowsService())
                        {
                            options.Mode = RemoteViewerMode.WindowsService;
                        }
                    });
            services.AddHostedService<TrackActiveSessionsBackgroundService>();
            services.AddHostedService<TrackInputDesktopBackgroundService>();
            services.AddSingleton<ScreenshotService>();
            services.AddWindowsService();
        })
        .UseNLog(new NLogProviderOptions
        {
            ReplaceLoggerFactory = true,
        });
}

public class RemoteViewerOptions
{
    public RemoteViewerMode Mode { get; set; } = RemoteViewerMode.Undefined;
    public uint? SessionId { get; set; }
}

public enum RemoteViewerMode
{
    Undefined,
    WindowsService,
    SessionRecorder,
}
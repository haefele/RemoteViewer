using System.Diagnostics;
using Avalonia;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using RemoteViewer.Client.Services;
using Serilog;
using Serilog.Events;

namespace RemoteViewer.Client;

enum ApplicationMode
{
    Desktop,
    WindowsService,
    SessionRecorder,
}

sealed class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        var mode = DetectApplicationMode(args);

        ConfigureLogging(mode);

        try
        {
            return mode switch
            {
                ApplicationMode.Desktop => RunAsDesktopApp(args),
                ApplicationMode.WindowsService or ApplicationMode.SessionRecorder => await RunAsHostedService(args, mode),
                _ => throw new InvalidOperationException($"Unknown application mode: {mode}"),
            };
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Application terminated unexpectedly");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static ApplicationMode DetectApplicationMode(string[] args)
    {
        if (args.Contains("--session-recorder"))
            return ApplicationMode.SessionRecorder;

        if (WindowsServiceHelpers.IsWindowsService())
            return ApplicationMode.WindowsService;

        return ApplicationMode.Desktop;
    }

    private static void ConfigureLogging(ApplicationMode mode)
    {
        var logPath = mode switch
        {
            ApplicationMode.WindowsService => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Remote Viewer Service",
                "Logs",
                "log-.txt"),

            ApplicationMode.SessionRecorder => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Remote Viewer Service",
                "Logs",
                $"session-{Process.GetCurrentProcess().SessionId}-log-.txt"),

            _ => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Remote Viewer Client",
                "Logs",
                "log-.txt"),
        };

        Log.Logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Warning)
            .WriteTo.Async(a => a.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
            .WriteTo.Async(a => a.Debug(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"))
            .CreateLogger();
    }

    private static async Task<int> RunAsHostedService(string[] args, ApplicationMode mode)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddHostedModeServices(mode);

        var host = builder.Build();
        await host.RunAsync();
        return 0;
    }

    private static int RunAsDesktopApp(string[] args)
    {
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.WinServ.Options;
using RemoteViewer.WinServ.Services;
using Serilog;
using ZiggyCreatures.Caching.Fusion;

try
{
    var builder = Host.CreateApplicationBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

    // Options
    builder.Services.AddOptions<RemoteViewerOptions>().Bind(builder.Configuration.GetSection("RemoteViewer"));

    // Hosted Services
    builder.Services.AddHostedService<TrackActiveSessionsBackgroundService>();
    builder.Services.AddHostedService<SessionRecorderRpcHostService>();

    // Services
    builder.Services.AddFusionCache();
    builder.Services.AddSingleton<IWin32Service, Win32Service>();
    builder.Services.AddSingleton<IDisplayService, WindowsDisplayService>();
    builder.Services.AddSingleton<IScreenGrabber, DxgiScreenGrabber>();
    builder.Services.AddSingleton<IScreenGrabber, BitBltScreenGrabber>();
    builder.Services.AddSingleton<IScreenshotService, ScreenshotService>();
    builder.Services.AddSingleton<IInputInjectionService, WindowsInputInjectionService>();
    builder.Services.AddSingleton<SessionRecorderRpcServer>();
    builder.Services.AddWindowsService();
    builder.Services.AddSerilog();

    var host = builder.Build();
    await host.RunAsync();
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Host terminated unexpectedly");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}
using RemoteViewer.WinServ.Options;
using RemoteViewer.WinServ.Services;
using Serilog;

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

    // Services
    builder.Services.AddSingleton<IWin32Service, Win32Service>();
    builder.Services.AddSingleton<DxgiScreenGrabber>();
    builder.Services.AddSingleton<BitBltScreenGrabber>();
    builder.Services.AddSingleton<IScreenshotService, ScreenshotService>();
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
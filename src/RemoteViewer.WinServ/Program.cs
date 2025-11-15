using RemoteViewer.WinServ.Options;
using RemoteViewer.WinServ.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddOptions<RemoteViewerOptions>().Bind(builder.Configuration.GetSection("RemoteViewer"));
builder.Services.AddHostedService<TrackActiveSessionsBackgroundService>();
builder.Services.AddSingleton<IWin32Service, Win32Service>();
builder.Services.AddWindowsService();

var host = builder.Build();
host.Run();
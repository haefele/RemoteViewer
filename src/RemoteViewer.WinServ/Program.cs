using RemoteViewer.WinServ;
using RemoteViewer.WinServ.Services;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<TrackActiveSessionsBackgroundService>();

var host = builder.Build();
host.Run();

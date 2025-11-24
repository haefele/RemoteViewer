using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IConnectionsService, ConnectionsService>();

var app = builder.Build();

app.MapHub<ConnectionHub>("/connection");

app.Run();

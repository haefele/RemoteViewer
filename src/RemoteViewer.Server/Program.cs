using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IConnectionsService, ConnectionsService>();
builder.Services.AddSignalR();

var app = builder.Build();

app.MapHub<ConnectionHub>("/connection");

app.Run();

using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IClientsService, ClientsService>();
builder.Services.AddSingleton<IConnectionsService, ConnectionService>();

var app = builder.Build();

app.MapHub<ConnectionHub>("/connection");

app.Run();

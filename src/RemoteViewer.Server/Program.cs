using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IClientIdGenerator, ClientIdGenerator>();

var app = builder.Build();

app.MapHub<ClientHub>("/clients");

app.Run();

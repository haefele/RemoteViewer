using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

    builder.Services.AddSingleton<IConnectionsService, ConnectionsService>();
    builder.Services.AddSignalR();
    builder.Services.AddSerilog();

    var app = builder.Build();

    app.MapHub<ConnectionHub>("/connection");

    await app.RunAsync();
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

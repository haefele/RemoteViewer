using Nerdbank.MessagePack.SignalR;
using PolyType.ReflectionProvider;
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
    builder.Services
        .AddSignalR(f =>
        {
            f.MaximumReceiveMessageSize = null;
        })
        .AddMessagePackProtocol(ReflectionTypeShapeProvider.Default);
    builder.Services.AddSerilog();

    var app = builder.Build();

    app.MapHub<ConnectionHub>("/connection");
    app.MapGet("/", () => "RemoteViewer Server is running.");

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

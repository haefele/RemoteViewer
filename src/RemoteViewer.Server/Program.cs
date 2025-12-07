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

    app.UseStaticFiles();

    app.MapHub<ConnectionHub>("/connection");

    app.MapGet("/", () => Results.Content("""
        <!DOCTYPE html>
        <html>
        <head>
            <title>RemoteViewer Server</title>
            <style>
                body { font-family: system-ui, sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background: #1a1a2e; color: #eee; }
                .container { text-align: center; }
                h1 { margin-bottom: 2rem; }
                a.download { display: inline-block; padding: 1rem 2rem; background: #4a90d9; color: white; text-decoration: none; border-radius: 8px; font-size: 1.1rem; }
                a.download:hover { background: #357abd; }
            </style>
        </head>
        <body>
            <div class="container">
                <h1>RemoteViewer Server</h1>
                <a class="download" href="/RemoteViewer-win-x64.zip" download>Download Client (Windows x64)</a>
            </div>
        </body>
        </html>
        """, "text/html"));

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

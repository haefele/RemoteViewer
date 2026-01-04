extern alias Server;

using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nerdbank.MessagePack.SignalR;
using Server::RemoteViewer.Server.Hubs;
using Server::RemoteViewer.Server.Services;
using RemoteViewer.Shared;

namespace RemoteViewer.IntegrationTests;

public class ServerFixture : IAsyncDisposable
{
    private WebApplication? _app;

    public string ServerUrl { get; private set; } = null!;

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0); // Random available port
        });

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IConnectionsService, ConnectionsService>();
        builder.Services.AddSingleton<IIpcTokenService, IpcTokenService>();
        builder.Services.AddControllers();
        builder.Services
            .AddSignalR(f => f.MaximumReceiveMessageSize = null)
            .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider);

        builder.Services.AddLogging(b =>
        {
            b.ClearProviders();
            b.AddConsole();
            b.SetMinimumLevel(LogLevel.Warning);
        });

        this._app = builder.Build();

        this._app.MapControllers();
        this._app.MapHub<ConnectionHub>("/connection");

        await this._app.StartAsync();

        // Get the actual bound address
        var server = this._app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();
        this.ServerUrl = addresses!.Addresses.First();
    }

    public async ValueTask DisposeAsync()
    {
        if (this._app is not null)
        {
            await this._app.StopAsync();
            await this._app.DisposeAsync();
        }
    }
}

using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Nerdbank.MessagePack.SignalR;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Server.Services;
using RemoteViewer.Shared;
using Serilog;

try
{
    var builder = WebApplication.CreateBuilder(args);

    Log.Logger = new LoggerConfiguration()
        .Enrich.FromLogContext()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

    builder.Host.UseOrleans(silo =>
    {
        silo.UseLocalhostClustering();

        var storageConnectionString = builder.Configuration["Orleans:Storage:ConnectionString"];
        var storageInvariant = builder.Configuration["Orleans:Storage:ProviderInvariant"] ?? "Npgsql";
        if (string.IsNullOrWhiteSpace(storageConnectionString))
        {
            silo.AddMemoryGrainStorage("Default");
        }
        else
        {
            silo.AddAdoNetGrainStorage("Default", options =>
            {
                options.Invariant = storageInvariant;
                options.ConnectionString = storageConnectionString;
            });
        }
    });

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IConnectionsService, ConnectionsOrleansService>();
    builder.Services.AddSingleton<IIpcTokenService, IpcTokenService>();
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            var jwtSection = builder.Configuration.GetSection("Jwt");
            var issuer = jwtSection["Issuer"];
            var audience = jwtSection["Audience"];
            var signingKey = jwtSection["SigningKey"];

            if (string.IsNullOrWhiteSpace(issuer) ||
                string.IsNullOrWhiteSpace(audience) ||
                string.IsNullOrWhiteSpace(signingKey))
            {
                throw new InvalidOperationException("Jwt configuration missing (Jwt:Issuer, Jwt:Audience, Jwt:SigningKey).");
            }

            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey))
            };

            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    if (!string.IsNullOrEmpty(accessToken) &&
                        context.HttpContext.Request.Path.StartsWithSegments("/connection"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                }
            };
        });
    builder.Services.AddAuthorization();
    builder.Services.AddControllers();
    builder.Services
        .AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = null;
        })
        .AddMessagePackProtocol(Witness.GeneratedTypeShapeProvider);
    builder.Services.AddSerilog();

    var app = builder.Build();

    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
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

public partial class Program { }

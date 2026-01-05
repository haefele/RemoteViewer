using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RemoteViewer.Client;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.Clipboard;
using RemoteViewer.Client.Services.Dialogs;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.LocalInputMonitor;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.SessionRecorderIpc;
using RemoteViewer.Client.Services.WinServiceIpc;
using RemoteViewer.IntegrationTests.Mocks;
using RemoteViewer.Server.Tests;

namespace RemoteViewer.IntegrationTests.Fixtures;

public class ClientFixture : IAsyncDisposable
{
    private static readonly Lock ServiceRegistrationLock = new();
    private readonly IServiceProvider _serviceProvider;

    public ConnectionHubClient HubClient { get; }
    public NullInputInjectionService InputInjectionService { get; }

    public ClientFixture(ServerFixture serverFixture, string? displayName = null)
    {
        this.InputInjectionService = new NullInputInjectionService();
        var inputService = this.InputInjectionService;

        // Lock to prevent race conditions with parallel tests
        using (ServiceRegistrationLock.EnterScope())
        {
            // Use ServiceRegistration.CustomizeServices to override dependencies
            ServiceRegistration.CustomizeServices = services =>
            {
                // Configure options with TestServer's handler
                services.Configure<ConnectionHubClientOptions>(options =>
                {
                    options.BaseUrl = serverFixture.TestServer.BaseAddress.ToString().TrimEnd('/');
                    options.HttpMessageHandlerFactory = () => serverFixture.TestServer.CreateHandler();
                });

                // Replace Avalonia-specific services with test implementations
                services.AddSingleton<IDispatcher, TestDispatcher>();
                services.AddSingleton<IClipboardService, NullClipboardService>();
                services.AddSingleton<IDialogService, NullDialogService>();
                services.AddSingleton<IDisplayService, NullDisplayService>();
                services.AddSingleton<IScreenshotService, NullScreenshotService>();
                services.AddSingleton<IInputInjectionService>(inputService);
                services.AddSingleton<ILocalInputMonitorService, NullLocalInputMonitorService>();

                // Create real RPC client instances with null loggers
                // (they'll fail to connect to non-existent pipes, which is fine for tests)
                var nullSessionLogger = Substitute.For<ILogger<SessionRecorderRpcClient>>();
                var nullWinServiceLogger = Substitute.For<ILogger<WinServiceRpcClient>>();
                services.AddSingleton(new SessionRecorderRpcClient(nullSessionLogger));
                services.AddSingleton(new WinServiceRpcClient(nullWinServiceLogger));
            };

            var services = new ServiceCollection();
            services.AddRemoteViewerServices(ApplicationMode.Desktop, app: null!);

            this._serviceProvider = services.BuildServiceProvider();
            this.HubClient = this._serviceProvider.GetRequiredService<ConnectionHubClient>();

            // Clear CustomizeServices after building to avoid affecting other tests
            ServiceRegistration.CustomizeServices = null;
        }

        if (displayName != null)
            _ = this.HubClient.SetDisplayName(displayName);
    }

    public async Task<(string Username, string Password)> WaitForCredentialsAsync(TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<(string, string)>();
        this.HubClient.CredentialsAssigned += (s, e) => tcs.TrySetResult((e.Username, e.Password));

        if (this.HubClient.Username != null)
            return (this.HubClient.Username, this.HubClient.Password!);

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public async Task<Connection> WaitForConnectionAsync(TimeSpan? timeout = null)
    {
        var tcs = new TaskCompletionSource<Connection>();
        this.HubClient.ConnectionStarted += (s, e) => tcs.TrySetResult(e.Connection);

        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        cts.Token.Register(() => tcs.TrySetCanceled());
        return await tcs.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await this.HubClient.DisposeAsync();
        if (this._serviceProvider is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }
}

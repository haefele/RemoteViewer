using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using RemoteViewer.Server.Grains;
using TUnit.Core.Interfaces;

namespace RemoteViewer.IntegrationTests.Fixtures;

public class ServerFixture : WebApplicationFactory<Program>, IAsyncInitializer
{
    public async Task InitializeAsync()
    {
        this.UseKestrel(port: 0);
        this.StartServer();
        await this.WaitForOrleansAsync();
    }

    private async Task WaitForOrleansAsync()
    {
        var grainFactory = this.Services.GetRequiredService<IGrainFactory>();

        var maxAttempts = 60;
        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                // Warmup with UsernameGrain and ClientGrain activations
                var usernameGrain = grainFactory.GetGrain<IUsernameGrain>("warmup-test");
                await usernameGrain.GetSignalrConnectionIdAsync();

                return;
            }
            catch
            {
                await Task.Delay(1000);
            }
        }

        throw new TimeoutException("Orleans silo did not become ready within the timeout period");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Warning);
        });
    }

    public async Task<ClientFixture> CreateClientAsync(string? displayName = null)
    {
        var client = new ClientFixture(this, displayName);
        await client.HubClient.ConnectToHub();
        return client;
    }

    public async Task CreateConnectionAsync(ClientFixture presenter, params ClientFixture[] viewers)
    {
        var (username, password) = await presenter.WaitForCredentialsAsync();

        var presenterConnTask = presenter.WaitForConnectionAsync();
        var viewerConnTasks = viewers.Select(v => v.WaitForConnectionAsync()).ToArray();

        foreach (var viewer in viewers)
        {
            var error = await viewer.HubClient.ConnectTo(username, password);
            if (error != null)
                throw new InvalidOperationException($"Connection failed: {error}");
        }

        await presenterConnTask;
        await Task.WhenAll(viewerConnTasks);
    }
}

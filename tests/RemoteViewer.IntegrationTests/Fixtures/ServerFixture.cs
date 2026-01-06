using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace RemoteViewer.IntegrationTests.Fixtures;

public class ServerFixture : WebApplicationFactory<Program>, IAsyncInitializer
{
    public TestServer TestServer => this.Server;

    public Task InitializeAsync()
    {
        // Eagerly initialize the server before tests run
        // This ensures the server is ready when parallel tests start
        // When parallel tests access this.Server for the first time, it may cause race conditions
        // This call forces the server to start during initialization once
        _ = this.Server;
        return Task.CompletedTask;
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

    public async Task CreateConnectionAsync(
        ClientFixture presenter,
        params ClientFixture[] viewers)
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

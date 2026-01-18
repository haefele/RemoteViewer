using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace RemoteViewer.TestFixtures.Fixtures;

public class ServerFixture : WebApplicationFactory<Program>, IAsyncInitializer
{
    public Task InitializeAsync()
    {
        this.UseKestrel(port: 0);
        this.StartServer();
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

        var presenterConn = await presenterConnTask;
        await Task.WhenAll(viewerConnTasks);

        // Wait for presenter to see all viewers (ConnectionChanged propagation)
        // This ensures GetViewerIdsWatchingDisplayAsync can find all viewers
        await RemoteViewer.TestFixtures.TestHelpers.WaitUntilAsync(
            () => presenterConn.Viewers.Count >= viewers.Length,
            message: $"Presenter did not see all {viewers.Length} viewers within timeout. Only saw {presenterConn.Viewers.Count}.");
    }
}

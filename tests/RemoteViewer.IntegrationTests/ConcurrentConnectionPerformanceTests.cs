using System.Diagnostics;
using RemoteViewer.IntegrationTests.Fixtures;
using TUnit.Core;

namespace RemoteViewer.IntegrationTests;

public class ConcurrentConnectionPerformanceTests
{
    // PerClass ensures this test class gets its own ServerFixture instance,
    // isolated from ConnectionHubClientTests which uses PerTestSession
    [ClassDataSource<ServerFixture>(Shared = SharedType.PerClass)]
    public required ServerFixture Server { get; init; }

    [Test]
    public async Task ManyClientPairsConnectConcurrentlyAndEstablishConnections()
    {
        const int PairCount = 20;

        var stopwatch = Stopwatch.StartNew();

        // Phase 1: Create all clients concurrently
        var clientTasks = Enumerable.Range(0, PairCount * 2)
            .Select(i => this.Server.CreateClientAsync($"Client{i}"))
            .ToArray();

        var clients = await Task.WhenAll(clientTasks);
        var creationTime = stopwatch.Elapsed;

        // Phase 2: Wait for all credentials to be assigned
        var credentialTasks = clients
            .Select(c => c.WaitForCredentialsAsync())
            .ToArray();

        var credentials = await Task.WhenAll(credentialTasks);
        var credentialTime = stopwatch.Elapsed;

        // Phase 3: Pair up and connect (even index = presenter, odd index = viewer)
        var connectionTasks = new List<Task>();
        for (var i = 0; i < PairCount; i++)
        {
            var presenter = clients[i * 2];
            var viewer = clients[i * 2 + 1];
            var (username, password) = credentials[i * 2]; // presenter's credentials

            connectionTasks.Add(viewer.HubClient.ConnectTo(username, password));
        }

        await Task.WhenAll(connectionTasks);
        var connectionTime = stopwatch.Elapsed;

        // Output timing metrics via TUnit's output writer
        var output = TestContext.Current?.OutputWriter ?? Console.Out;
        await output.WriteLineAsync($"=== Concurrent Connection Performance ===");
        await output.WriteLineAsync($"Pair count: {PairCount} ({PairCount * 2} clients total)");
        await output.WriteLineAsync($"Phase 1 - Clients created:         {creationTime.TotalMilliseconds:F1}ms");
        await output.WriteLineAsync($"Phase 2 - Credentials received:    {credentialTime.TotalMilliseconds:F1}ms");
        await output.WriteLineAsync($"Phase 3 - Connections established: {connectionTime.TotalMilliseconds:F1}ms");
        await output.WriteLineAsync($"==========================================");

        // Verify all clients got unique credentials
        var uniqueUsernames = credentials.Select(c => c.Username).Distinct().Count();
        await Assert.That(uniqueUsernames).IsEqualTo(PairCount * 2);

        // Cleanup
        foreach (var client in clients)
        {
            await client.DisposeAsync();
        }
    }
}

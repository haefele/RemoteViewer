using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using RemoteViewer.Server.Common;

namespace RemoteViewer.Server.Tests.Common;

public interface ITestHubClient
{
    Task TestMethod(string message);
    Task TestMethod2(int value);
}

public class TestHub : Hub<ITestHubClient>;

public class BatchedHubActionsTests
{
    private IHubContext<TestHub, ITestHubClient> _hubContext = null!;
    private ILogger _logger = null!;
    private ITestHubClient _mockClient = null!;
    private IHubClients<ITestHubClient> _mockClients = null!;

    [Before(Test)]
    public void Setup()
    {
        this._hubContext = Substitute.For<IHubContext<TestHub, ITestHubClient>>();
        this._logger = Substitute.For<ILogger>();
        this._mockClient = Substitute.For<ITestHubClient>();
        this._mockClients = Substitute.For<IHubClients<ITestHubClient>>();

        this._hubContext.Clients.Returns(this._mockClients);
        this._mockClients.All.Returns(this._mockClient);
    }

    [Test]
    public async Task AddSingleActionExecutesOnExecuteAll()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);

        batch.Add(clients => clients.All.TestMethod("hello"));

        // Act
        await batch.ExecuteAll();

        // Assert
        await this._mockClient.Received(1).TestMethod("hello");
    }

    [Test]
    public async Task AddMultipleActionsExecutesAllInOrder()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);
        var callOrder = new List<string>();

        this._mockClient.TestMethod(Arg.Any<string>())
            .Returns(Task.CompletedTask)
            .AndDoes(c => callOrder.Add(c.Arg<string>()));

        batch.Add(clients => clients.All.TestMethod("first"));
        batch.Add(clients => clients.All.TestMethod("second"));
        batch.Add(clients => clients.All.TestMethod("third"));

        // Act
        await batch.ExecuteAll();

        // Assert
        await Assert.That(callOrder).Count().IsEqualTo(3);
        await Assert.That(callOrder[0]).IsEqualTo("first");
        await Assert.That(callOrder[1]).IsEqualTo("second");
        await Assert.That(callOrder[2]).IsEqualTo("third");
    }

    [Test]
    public async Task ExecuteAllClearsActionsAfterExecution()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);

        batch.Add(clients => clients.All.TestMethod("hello"));
        await batch.ExecuteAll();

        this._mockClient.ClearReceivedCalls();

        // Act - Second execute should not call anything
        await batch.ExecuteAll();

        // Assert
        await this._mockClient.DidNotReceive().TestMethod(Arg.Any<string>());
    }

    [Test]
    public async Task ExecuteAllExceptionInActionContinuesOtherActions()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);

        // First action throws
        this._mockClient.TestMethod("fail")
            .Returns(Task.FromException(new InvalidOperationException("Client disconnected")));

        // Second action succeeds
        this._mockClient.TestMethod("success")
            .Returns(Task.CompletedTask);

        batch.Add(clients => clients.All.TestMethod("fail"));
        batch.Add(clients => clients.All.TestMethod("success"));

        // Act - Should not throw
        await batch.ExecuteAll();

        // Assert - Both should have been called
        await this._mockClient.Received(1).TestMethod("fail");
        await this._mockClient.Received(1).TestMethod("success");
    }

    [Test]
    public async Task ExecuteAllWithNoActionsDoesNothing()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);

        // Act - Should not throw
        await batch.ExecuteAll();

        // Assert
        await this._mockClient.DidNotReceive().TestMethod(Arg.Any<string>());
    }

    [Test]
    public async Task AddDifferentActionTypesExecutesAll()
    {
        var batch = new BatchedHubActions<TestHub, ITestHubClient>(this._hubContext, this._logger);

        batch.Add(clients => clients.All.TestMethod("message"));
        batch.Add(clients => clients.All.TestMethod2(42));

        // Act
        await batch.ExecuteAll();

        // Assert
        await this._mockClient.Received(1).TestMethod("message");
        await this._mockClient.Received(1).TestMethod2(42);
    }
}

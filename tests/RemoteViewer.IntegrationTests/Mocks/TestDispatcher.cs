using RemoteViewer.Client.Services.Dispatching;

namespace RemoteViewer.IntegrationTests.Mocks;

public class TestDispatcher : IDispatcher
{
    public void Post(Action action) => action();
    public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
}

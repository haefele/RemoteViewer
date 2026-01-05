using Avalonia.Threading;

namespace RemoteViewer.Client.Services;

public sealed class AvaloniaDispatcher : IDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
}

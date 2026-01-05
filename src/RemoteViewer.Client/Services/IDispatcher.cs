namespace RemoteViewer.Client.Services;

public interface IDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Action action);
}

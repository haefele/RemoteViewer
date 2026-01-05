namespace RemoteViewer.Client.Services.Dispatching;

public interface IDispatcher
{
    void Post(Action action);
    Task InvokeAsync(Action action);
}

namespace RemoteViewer.Client.Services.Dialogs;

public interface IWindowHandle
{
    event EventHandler? Closed;

    void Show();
    void Activate();
    void Close();
}

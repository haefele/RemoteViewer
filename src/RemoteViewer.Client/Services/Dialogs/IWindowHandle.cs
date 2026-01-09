namespace RemoteViewer.Client.Services.Dialogs;

public interface IWindowHandle
{
    event EventHandler? Closed;

    bool IsClosed { get; }

    void Show();
    void Activate();
    void Close();
}


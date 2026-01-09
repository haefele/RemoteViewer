using Avalonia.Controls;

namespace RemoteViewer.Client.Services.Dialogs;

internal sealed class WindowHandle : IWindowHandle
{
    private Window? _window;

    public WindowHandle(Window window)
    {
        this._window = window;
        this._window.Closed += this.OnWindowClosed;
    }

    public event EventHandler? Closed;

    public void Show()
    {
        if (this._window is null)
            throw new InvalidOperationException("Window is closed");

        this._window.Show();
    }

    public void Activate()
    {
        if (this._window is null)
            throw new InvalidOperationException("Window is closed");

        this._window.Activate();
    }

    public void Close()
    {
        if (this._window is null)
            throw new InvalidOperationException("Window is closed");

        this._window.Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.Closed?.Invoke(this, EventArgs.Empty);

        this._window?.Closed -= this.OnWindowClosed;
        this._window = null;
    }
}

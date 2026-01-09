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

    public bool IsClosed => this._window is null;

    public void Show()
    {
        if (this._window is null)
            return;

        this._window.Show();
    }

    public void Activate()
    {
        if (this._window is null)
            return;

        this._window.Activate();
    }

    public void Close()
    {
        if (this._window is null)
            return;

        this._window.Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        this.Closed?.Invoke(this, EventArgs.Empty);

        if (this._window is { } window)
            window.Closed -= this.OnWindowClosed;

        this._window = null;
    }
}

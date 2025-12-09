namespace RemoteViewer.Client.Services.Toasts;

public interface IToastService
{
    event EventHandler<ToastEventArgs>? ToastRequested;

    void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000);
    void Success(string message, int durationMs = 3000);
    void Error(string message, int durationMs = 4000);
    void Info(string message, int durationMs = 3000);
}

public class ToastService : IToastService
{
    public event EventHandler<ToastEventArgs>? ToastRequested;

    public void Show(string message, ToastType type = ToastType.Info, int durationMs = 3000)
    {
        this.ToastRequested?.Invoke(this, new ToastEventArgs(message, type, durationMs));
    }

    public void Success(string message, int durationMs = 3000) =>
        this.Show(message, ToastType.Success, durationMs);

    public void Error(string message, int durationMs = 4000) =>
        this.Show(message, ToastType.Error, durationMs);

    public void Info(string message, int durationMs = 3000) =>
        this.Show(message, ToastType.Info, durationMs);
}

public class ToastEventArgs(string message, ToastType type, int durationMs) : EventArgs
{
    public string Message { get; } = message;
    public ToastType Type { get; } = type;
    public int DurationMs { get; } = durationMs;
}

public enum ToastType
{
    Info,
    Success,
    Error
}

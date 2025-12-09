using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using RemoteViewer.Client.Services.Toasts;

namespace RemoteViewer.Client.Controls;

public partial class ToastBar : UserControl, IDisposable
{
    private readonly IToastService? _toastService;
    private CancellationTokenSource? _hideTokenSource;
    private bool _disposed;

    public ToastBar()
    {
        this.InitializeComponent();

        // Get toast service from App's service provider
        this._toastService = App.Current.Services.GetRequiredService<IToastService>();
        this._toastService.ToastRequested += this.OnToastRequested;
    }

    private void OnToastRequested(object? sender, ToastEventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.ShowToast(e.Message, e.Type, e.DurationMs));
    }

    private void ShowToast(string message, ToastType type, int durationMs)
    {
        var border = this.FindControl<Border>("ToastBorder");
        var iconText = this.FindControl<TextBlock>("IconText");
        var messageText = this.FindControl<TextBlock>("MessageText");

        if (border is null || iconText is null || messageText is null)
            return;

        var (background, icon) = type switch
        {
            ToastType.Success => (Color.Parse("#4CAF50"), "\u2713"),
            ToastType.Error => (Color.Parse("#F44336"), "\u2717"),
            ToastType.Info => (Color.Parse("#2196F3"), "\u2139"),
            _ => (Color.Parse("#2196F3"), "\u2139")
        };

        border.Background = new SolidColorBrush(background);
        iconText.Text = icon;
        messageText.Text = message;
        this.IsVisible = true;

        this.StartAutoHideTimer(durationMs);
    }

    private void StartAutoHideTimer(int durationMs)
    {
        this._hideTokenSource?.Cancel();
        this._hideTokenSource = new CancellationTokenSource();

        var token = this._hideTokenSource.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(durationMs, token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        this.IsVisible = false;
                    }
                });
            }
            catch (TaskCanceledException)
            {
            }
        }, token);
    }

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        this._hideTokenSource?.Cancel();
        this.IsVisible = false;
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        if (this._toastService is not null)
        {
            this._toastService.ToastRequested -= this.OnToastRequested;
        }

        this._hideTokenSource?.Cancel();
        this._hideTokenSource?.Dispose();
        this._disposed = true;
    }
}

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Client.Services.ViewModels;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IDisposable
{
    private readonly Connection _connection;
    private readonly IDisplayService _displayService;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILogger<PresenterViewModel> _logger;

    public ToastsViewModel Toasts { get; }

    private DisplayCaptureManager? _captureManager;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "Presenting";

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private bool _isPresenting = true;

    [ObservableProperty]
    private string _statusText = "Waiting for viewers...";

    [ObservableProperty]
    private bool _isPlatformSupported;

    public event EventHandler? CloseRequested;

    public PresenterViewModel(
        Connection connection,
        IDisplayService displayService,
        IScreenshotService screenshotService,
        ScreenEncoder screenEncoder,
        IInputInjectionService inputInjectionService,
        IViewModelFactory viewModelFactory,
        ILogger<PresenterViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._inputInjectionService = inputInjectionService;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        this.IsPlatformSupported = screenshotService.IsSupported;

        if (!this.IsPlatformSupported)
        {
            this.Title = "Presenter - Not Supported";
            this.StatusText = "Not supported on this platform";
            this.IsPresenting = false;
            return;
        }

        this.Title = $"Presenting - {connection.ConnectionId[..8]}...";

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.InputReceived += this.OnInputReceived;
        this._connection.Closed += this.OnConnectionClosed;

        // Create and start capture manager
        this._captureManager = new DisplayCaptureManager(
            connection,
            displayService,
            screenshotService,
            screenEncoder,
            loggerFactory,
            loggerFactory.CreateLogger<DisplayCaptureManager>());
        this._captureManager.Start();
    }

    private void OnViewersChanged(object? sender, EventArgs e)
    {
        var viewers = this._connection.Viewers;

        // Log new/removed viewers for debugging
        this._logger.LogInformation("Viewer list changed: {ViewerCount} viewer(s)", viewers.Count);

        // Update UI
        Dispatcher.UIThread.Post(() =>
        {
            this.ViewerCount = viewers.Count;
            this.StatusText = this.ViewerCount == 0
                ? "Waiting for viewers..."
                : $"{this.ViewerCount} viewer(s) connected";
        });
    }

    private void OnInputReceived(object? sender, InputReceivedEventArgs e)
    {
        // Get the display for this viewer's selection
        if (e.DisplayId is null)
            return;

        var display = this.GetDisplayById(e.DisplayId);
        if (display is null)
            return;

        switch (e.Type)
        {
            case InputType.MouseMove:
                if (e.X.HasValue && e.Y.HasValue)
                {
                    this._inputInjectionService.InjectMouseMove(display, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseDown:
                if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                {
                    this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: true, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseUp:
                if (e.X.HasValue && e.Y.HasValue && e.Button.HasValue)
                {
                    this._inputInjectionService.InjectMouseButton(display, e.Button.Value, isDown: false, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.MouseWheel:
                if (e.X.HasValue && e.Y.HasValue && e.DeltaX.HasValue && e.DeltaY.HasValue)
                {
                    this._inputInjectionService.InjectMouseWheel(display, e.DeltaX.Value, e.DeltaY.Value, e.X.Value, e.Y.Value);
                }
                break;

            case InputType.KeyDown:
                if (e.KeyCode.HasValue)
                {
                    this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: true);
                }
                break;

            case InputType.KeyUp:
                if (e.KeyCode.HasValue)
                {
                    this._inputInjectionService.InjectKey(e.KeyCode.Value, isDown: false);
                }
                break;
        }
    }

    private void OnConnectionClosed(object? sender, EventArgs e)
    {
        this._captureManager?.Dispose();
        this._captureManager = null;

        // Release any stuck modifier keys when connection closes
        this._inputInjectionService.ReleaseAllModifiers();

        Dispatcher.UIThread.Post(() =>
        {
            this.IsPresenting = false;
            this.StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private Display? GetDisplayById(string displayId)
    {
        return this._displayService.GetDisplays().FirstOrDefault(d => d.Name == displayId);
    }

    [RelayCommand]
    private async Task StopPresentingAsync()
    {
        if (this.IsPlatformSupported is false)
        {
            this.CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        this._logger.LogInformation("User requested to stop presenting for connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._captureManager?.Dispose();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.Closed -= this.OnConnectionClosed;

        GC.SuppressFinalize(this);
    }
}

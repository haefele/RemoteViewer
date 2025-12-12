using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IDisposable
{
    private readonly Connection _connection;
    private readonly IDisplayService _displayService;
    private readonly IScreenshotService _screenshotService;
    private readonly ScreenEncoder _screenEncoder;
    private readonly IInputInjectionService _inputInjectionService;
    private readonly ILogger<PresenterViewModel> _logger;

    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;
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
        ILogger<PresenterViewModel> logger)
    {
        this._connection = connection;
        this._displayService = displayService;
        this._screenshotService = screenshotService;
        this._screenEncoder = screenEncoder;
        this._inputInjectionService = inputInjectionService;
        this._logger = logger;

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

        // Start presenting immediately
        this.StartPresenting();
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
        this.StopPresenting();

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

    private void StartPresenting()
    {
        this._logger.LogInformation("Starting presenter for connection {ConnectionId}", this._connection.ConnectionId);

        this._captureLoopCts = new CancellationTokenSource();
        this._captureLoopTask = Task.Run(() => this.CaptureLoopAsync(this._captureLoopCts.Token));
    }

    private void StopPresenting()
    {
        this._captureLoopCts?.Cancel();
        this._logger.LogInformation("Stopped presenting for connection {ConnectionId}", this._connection.ConnectionId);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        const int TargetFps = 30;
        var frameInterval = TimeSpan.FromMilliseconds(1000.0 / TargetFps);
        ulong frameNumber = 0;

        this._logger.LogInformation("Capture loop started for connection {ConnectionId}", this._connection.ConnectionId);

        while (!ct.IsCancellationRequested)
        {
            var frameStart = Stopwatch.GetTimestamp();

            try
            {
                await this.CaptureAndSendFramesAsync(frameNumber++);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogError(ex, "Error in capture loop");
            }

            // Maintain target FPS
            SpinWait.SpinUntil(() =>
            {
                if (ct.IsCancellationRequested)
                    return true;

                var current = Stopwatch.GetTimestamp();
                var elapsed = TimeSpan.FromTicks((current - frameStart) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);

                var delay = frameInterval - elapsed;
                return delay.TotalMilliseconds <= 5;
            });
        }

        this._logger.LogInformation("Capture loop ended for connection {ConnectionId}", this._connection.ConnectionId);
    }

    private async Task CaptureAndSendFramesAsync(ulong frameNumber)
    {
        // Get unique displays that viewers are watching
        var viewers = this._connection.Viewers;
        var displayIds = viewers
            .Select(v => v.SelectedDisplayId)
            .Where(id => id is not null)
            .Distinct()
            .Cast<string>()
            .ToList();

        if (displayIds.Count == 0)
            return;

        var displays = this._displayService.GetDisplays();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const byte Quality = 75;

        foreach (var displayId in displayIds)
        {
            var display = displays.FirstOrDefault(d => d.Name == displayId);
            if (display is null)
                continue;

            var width = display.Bounds.Width;
            var height = display.Bounds.Height;

            // ScreenshotService now handles memory allocation and keyframe timing
            using var grabResult = this._screenshotService.CaptureDisplay(display);

            var encodeResult = this._screenEncoder.ProcessFrame(
                grabResult,
                width,
                height);

            if (encodeResult.HasChanges is false)
                continue;

            var regions = new FrameRegion[encodeResult.Regions.Length];
            for (var i = 0; i < encodeResult.Regions.Length; i++)
            {
                var region = encodeResult.Regions[i];
                regions[i] = new FrameRegion(region.X, region.Y, region.Width, region.Height, region.JpegData.Memory);
            }

            try
            {
                await this._connection.SendFrameAsync(
                    displayId!,
                    frameNumber,
                    timestamp,
                    FrameCodec.Jpeg,
                    width,
                    height,
                    Quality,
                    encodeResult.FrameType,
                    regions);
            }
            finally
            {
                foreach (var region in encodeResult.Regions)
                {
                    region.JpegData.Dispose();
                }
            }
        }
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

        // Stop capture loop and wait for it to complete
        this.StopPresenting();
        try
        {
            this._captureLoopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException) { } // Ignore cancellation exceptions

        this._captureLoopCts?.Dispose();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.InputReceived -= this.OnInputReceived;
        this._connection.Closed -= this.OnConnectionClosed;

        GC.SuppressFinalize(this);
    }
}

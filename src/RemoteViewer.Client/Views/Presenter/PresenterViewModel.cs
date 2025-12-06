using System.Diagnostics;
using System.Drawing;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Server.SharedAPI.Protocol;
using SkiaSharp;

namespace RemoteViewer.Client.Views.Presenter;

/// <summary>
/// ViewModel for the presenter window that handles presentation status, screen capture,
/// frame sending, and input injection.
/// </summary>
public partial class PresenterViewModel : ViewModelBase, IDisposable
{
    private readonly Connection _connection;
    private readonly IScreenshotService _screenshotService;
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
        IScreenshotService screenshotService,
        IInputInjectionService inputInjectionService,
        ILogger<PresenterViewModel> logger)
    {
        this._connection = connection;
        this._screenshotService = screenshotService;
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
        return this._screenshotService.GetDisplays().FirstOrDefault(d => d.Name == displayId);
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
            .ToList();

        if (displayIds.Count == 0)
            return;

        var displays = this._screenshotService.GetDisplays();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const byte Quality = 75;

        foreach (var displayId in displayIds)
        {
            var display = displays.FirstOrDefault(d => d.Name == displayId);
            if (display is null)
                continue;

            var captureResult = this._screenshotService.CaptureDisplay(display);
            if (captureResult is { Success: false } or { Bitmap: null })
                continue;

            var bitmap = captureResult.Bitmap;
            var dirtyRects = captureResult.DirtyRectangles;

            // Determine frame type: empty dirty rects means keyframe
            var isKeyframe = dirtyRects.Length == 0;
            var frameType = isKeyframe ? FrameType.Keyframe : FrameType.DeltaFrame;

            FrameRegion[] regions;
            if (isKeyframe)
            {
                // Full frame as single region
                var frameData = EncodeJpeg(bitmap, Quality);
                regions = [new FrameRegion(0, 0, bitmap.Width, bitmap.Height, frameData)];
            }
            else
            {
                // Encode each dirty region separately
                regions = new FrameRegion[dirtyRects.Length];
                for (var i = 0; i < dirtyRects.Length; i++)
                {
                    var rect = dirtyRects[i];
                    using var regionBitmap = ExtractRegion(bitmap, rect);
                    var regionData = EncodeJpeg(regionBitmap, Quality);
                    regions[i] = new FrameRegion(rect.X, rect.Y, rect.Width, rect.Height, regionData);
                }
            }

            // Send frame to viewers watching this display
            await this._connection.SendFrameAsync(
                displayId!,
                frameNumber,
                timestamp,
                FrameCodec.Jpeg,
                bitmap.Width,
                bitmap.Height,
                Quality,
                frameType,
                regions);
        }
    }

    private static SKBitmap ExtractRegion(SKBitmap source, Rectangle rect)
    {
        var region = new SKBitmap(rect.Width, rect.Height, source.ColorType, source.AlphaType);
        using var canvas = new SKCanvas(region);
        canvas.DrawBitmap(
            source,
            new SKRect(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height),
            new SKRect(0, 0, rect.Width, rect.Height));
        return region;
    }

    private static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
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

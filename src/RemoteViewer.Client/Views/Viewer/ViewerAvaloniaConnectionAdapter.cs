using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Win32.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;
using AvaloniaMouseButton = Avalonia.Input.MouseButton;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;
using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Server.SharedAPI.Protocol.KeyModifiers;

namespace RemoteViewer.Client.Views.Viewer;

public sealed class ViewerAvaloniaConnectionAdapter : IDisposable
{
    private readonly Connection _connection;
    private readonly ILogger<ViewerAvaloniaConnectionAdapter> _logger;
    private readonly FrameCompositor _compositor = new();
    private Control? _inputPanel;
    private Image? _frameImage;
    private Image? _debugOverlayImage;
    private bool _disposed;

    public ViewerAvaloniaConnectionAdapter(Connection connection, ILogger<ViewerAvaloniaConnectionAdapter> logger)
    {
        this._connection = connection;
        this._logger = logger;

        this._connection.RequiredViewerService.FrameReady += this.Service_FrameReady;
    }

    public void Attach(Control inputPanel, Image frameImage, Image debugOverlayImage)
    {
        if (ReferenceEquals(this._inputPanel, inputPanel))
            return;

        this.Detach();

        this._inputPanel = inputPanel;
        this._frameImage = frameImage;
        this._debugOverlayImage = debugOverlayImage;

        this._inputPanel.PointerMoved += this.Panel_PointerMoved;
        this._inputPanel.PointerPressed += this.Panel_PointerPressed;
        this._inputPanel.PointerReleased += this.Panel_PointerReleased;
        this._inputPanel.PointerWheelChanged += this.Panel_PointerWheelChanged;
        this._inputPanel.KeyDown += this.Panel_KeyDown;
        this._inputPanel.KeyUp += this.Panel_KeyUp;
    }

    public void Detach()
    {
        if (this._inputPanel is not null)
        {
            this._inputPanel.PointerMoved -= this.Panel_PointerMoved;
            this._inputPanel.PointerPressed -= this.Panel_PointerPressed;
            this._inputPanel.PointerReleased -= this.Panel_PointerReleased;
            this._inputPanel.PointerWheelChanged -= this.Panel_PointerWheelChanged;
            this._inputPanel.KeyDown -= this.Panel_KeyDown;
            this._inputPanel.KeyUp -= this.Panel_KeyUp;
        }

        this._inputPanel = null;
        this._frameImage = null;
        this._debugOverlayImage = null;
    }

    private bool IsInputEnabledNow() => this._connection.RequiredViewerService.IsInputEnabled;

    private async void Panel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!this.IsInputEnabledNow())
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            await this._connection.RequiredViewerService.SendMouseMoveAsync(x, y);
        }
    }

    private async void Panel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!this.IsInputEnabledNow())
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            var target = this._frameImage ?? (Control)sender!;
            var point = e.GetCurrentPoint(target);
            var button = this.GetMouseButton(point.Properties);
            if (button is not null)
            {
                await this._connection.RequiredViewerService.SendMouseDownAsync(button.Value, x, y);
            }
        }
    }

    private async void Panel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!this.IsInputEnabledNow())
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            var button = e.InitialPressMouseButton switch
            {
                AvaloniaMouseButton.Left => ProtocolMouseButton.Left,
                AvaloniaMouseButton.Right => ProtocolMouseButton.Right,
                AvaloniaMouseButton.Middle => ProtocolMouseButton.Middle,
                _ => (ProtocolMouseButton?)null
            };

            if (button is not null)
            {
                await this._connection.RequiredViewerService.SendMouseUpAsync(button.Value, x, y);
            }
        }
    }

    private async void Panel_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!this.IsInputEnabledNow())
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            await this._connection.RequiredViewerService.SendMouseWheelAsync((float)e.Delta.X, (float)e.Delta.Y, x, y);
        }
    }

    private async void Panel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
            return;

        if (!this.IsInputEnabledNow())
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._connection.RequiredViewerService.SendKeyDownAsync(keyCode, modifiers);
    }

    private async void Panel_KeyUp(object? sender, KeyEventArgs e)
    {
        if (!this.IsInputEnabledNow())
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._connection.RequiredViewerService.SendKeyUpAsync(keyCode, modifiers);
    }

    private void Service_FrameReady(object? sender, FrameReceivedEventArgs e)
    {
        try
        {
            if (e.Regions is [{ IsKeyframe: true }])
            {
                this._compositor.ApplyKeyframe(e.Regions, e.FrameNumber);
            }
            else
            {
                this._compositor.ApplyDeltaRegions(e.Regions, e.FrameNumber);
            }

            var frame = this._compositor.Canvas;
            var overlay = this._compositor.DebugOverlay;

            Dispatcher.UIThread.Post(() =>
            {
                if (this._frameImage is { } frameImage)
                {
                    frameImage.Source = frame;
                    frameImage.InvalidateVisual();
                }

                if (this._debugOverlayImage is { } overlayImage)
                {
                    overlayImage.Source = overlay;
                    overlayImage.IsVisible = overlay is not null;
                    overlayImage.InvalidateVisual();
                }
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error processing frame");
        }
    }

    private bool TryGetNormalizedPosition(PointerEventArgs e, out float x, out float y)
    {
        x = -1;
        y = -1;

        var frame = this._frameImage;
        if (frame is null)
            return false;

        var point = e.GetPosition(frame);
        var bounds = frame.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        x = (float)(point.X / bounds.Width);
        y = (float)(point.Y / bounds.Height);

        return x is >= 0 and <= 1 && y is >= 0 and <= 1;
    }

    private ProtocolMouseButton? GetMouseButton(PointerPointProperties properties) => properties switch
    {
        { IsLeftButtonPressed: true } => ProtocolMouseButton.Left,
        { IsRightButtonPressed: true } => ProtocolMouseButton.Right,
        { IsMiddleButtonPressed: true } => ProtocolMouseButton.Middle,
        _ => null,
    };

    private ProtocolKeyModifiers GetKeyModifiers(AvaloniaKeyModifiers modifiers)
    {
        var result = ProtocolKeyModifiers.None;

        if (modifiers.HasFlag(AvaloniaKeyModifiers.Shift))
            result |= ProtocolKeyModifiers.Shift;

        if (modifiers.HasFlag(AvaloniaKeyModifiers.Control))
            result |= ProtocolKeyModifiers.Control;

        if (modifiers.HasFlag(AvaloniaKeyModifiers.Alt))
            result |= ProtocolKeyModifiers.Alt;

        if (modifiers.HasFlag(AvaloniaKeyModifiers.Meta))
            result |= ProtocolKeyModifiers.Win;

        return result;
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._connection.RequiredViewerService.FrameReady -= this.Service_FrameReady;
        this.Detach();
        this._compositor.Dispose();
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Server.SharedAPI.Protocol.KeyModifiers;
using Avalonia.Win32.Input;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerView : Window
{
    private ViewerViewModel? _viewModel;

    public ViewerView()
    {
        this.InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (this._viewModel is not null)
        {
            this._viewModel.PropertyChanged -= this.ViewModelOnPropertyChanged;
            this._viewModel.CloseRequested -= this.OnCloseRequested;
        }

        this._viewModel = this.DataContext as ViewerViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.OnCloseRequested;
            this._viewModel.PropertyChanged += this.ViewModelOnPropertyChanged;
        }
    }

    private void ViewModelOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.FrameBitmap))
            this.FrameImage.InvalidateVisual();
        else if (e.PropertyName == nameof(ViewerViewModel.DebugOverlayBitmap))
            this.DebugOverlayImage.InvalidateVisual();
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            this._viewModel.SendMouseMove(x, y);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            var point = e.GetCurrentPoint(this.FrameImage);
            var button = GetMouseButton(point.Properties);
            if (button is not null)
            {
                this._viewModel.SendMouseDown(button.Value, x, y);
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Left => ProtocolMouseButton.Left,
                MouseButton.Right => ProtocolMouseButton.Right,
                MouseButton.Middle => ProtocolMouseButton.Middle,
                _ => (ProtocolMouseButton?)null
            };

            if (button is not null)
            {
                this._viewModel.SendMouseUp(button.Value, x, y);
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            this._viewModel.SendMouseWheel((float)e.Delta.X, (float)e.Delta.Y, x, y);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (this._viewModel is null)
            return;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = GetKeyModifiers(e.KeyModifiers);
        this._viewModel.SendKeyDown(keyCode, modifiers);

        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (this._viewModel is null)
            return;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = GetKeyModifiers(e.KeyModifiers);
        this._viewModel.SendKeyUp(keyCode, modifiers);

        e.Handled = true;
    }

    private (float X, float Y) GetNormalizedPosition(PointerEventArgs e)
    {
        var point = e.GetPosition(this.FrameImage);
        var bounds = this.FrameImage.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return (-1, -1);

        var x = (float)(point.X / bounds.Width);
        var y = (float)(point.Y / bounds.Height);

        return (x, y);
    }

    private static ProtocolMouseButton? GetMouseButton(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
            return ProtocolMouseButton.Left;
        if (properties.IsRightButtonPressed)
            return ProtocolMouseButton.Right;
        if (properties.IsMiddleButtonPressed)
            return ProtocolMouseButton.Middle;
        return null;
    }

    private static ProtocolKeyModifiers GetKeyModifiers(KeyModifiers modifiers)
    {
        var result = ProtocolKeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Shift))
            result |= ProtocolKeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Control))
            result |= ProtocolKeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt))
            result |= ProtocolKeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Meta))
            result |= ProtocolKeyModifiers.Win;
        return result;
    }
}

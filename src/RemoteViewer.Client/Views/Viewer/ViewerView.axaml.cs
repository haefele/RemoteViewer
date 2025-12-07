using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Win32.Input;
using System.ComponentModel;
using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Server.SharedAPI.Protocol.KeyModifiers;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerView : Window
{
    private ViewerViewModel? _viewModel;

    public ViewerView()
    {
        this.InitializeComponent();
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.PropertyChanged -= this.ViewModel_PropertyChanged;
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
        }

        this._viewModel = this.DataContext as ViewerViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.PropertyChanged += this.ViewModel_PropertyChanged;
        }
    }
    private void Window_Opened(object? sender, EventArgs e)
    {
        this.DisplayPanel.Focus();
    }
    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.ReleaseAllKeysAsync();
    }
    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.FrameBitmap))
            this.FrameImage.InvalidateVisual();

        if (e.PropertyName == nameof(ViewerViewModel.DebugOverlayBitmap))
            this.DebugOverlayImage.InvalidateVisual();
    }
    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    private async void DisplayPanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            await this._viewModel.SendMouseMoveAsync(x, y);
        }
    }
    private async void DisplayPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            var point = e.GetCurrentPoint(this.FrameImage);
            var button = this.GetMouseButton(point.Properties);
            if (button is not null)
            {
                await this._viewModel.SendMouseDownAsync(button.Value, x, y);
            }
        }
    }
    private async void DisplayPanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
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
                await this._viewModel.SendMouseUpAsync(button.Value, x, y);
            }
        }
    }
    private async void DisplayPanel_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            await this._viewModel.SendMouseWheelAsync((float)e.Delta.X, (float)e.Delta.Y, x, y);
        }
    }
    private async void DisplayPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (this._viewModel is null)
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.SendKeyDownAsync(keyCode, modifiers);
    }
    private async void DisplayPanel_KeyUp(object? sender, KeyEventArgs e)
    {
        if (this._viewModel is null)
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.SendKeyUpAsync(keyCode, modifiers);
    }

    private void DisplayComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        this.DisplayPanel.Focus();
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
    private ProtocolMouseButton? GetMouseButton(PointerPointProperties properties) => properties switch
    {
        { IsLeftButtonPressed: true } => ProtocolMouseButton.Left,
        { IsRightButtonPressed: true } => ProtocolMouseButton.Right,
        { IsMiddleButtonPressed: true } => ProtocolMouseButton.Middle,
        _ => null,
    };
    private ProtocolKeyModifiers GetKeyModifiers(KeyModifiers modifiers)
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

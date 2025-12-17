using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Win32.Input;
using System.ComponentModel;
using ProtocolMouseButton = RemoteViewer.Server.SharedAPI.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Server.SharedAPI.Protocol.KeyModifiers;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerView : Window
{
    #region Constructor
    private ViewerViewModel? _viewModel;

    public ViewerView()
    {
        this.InitializeComponent();
    }
    #endregion

    #region Lifecycle Events
    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.PropertyChanged -= this.ViewModel_PropertyChanged;
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            this._viewModel.OpenFilePickerRequested -= this.ViewModel_OpenFilePickerRequested;
        }

        this._viewModel = this.DataContext as ViewerViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.PropertyChanged += this.ViewModel_PropertyChanged;
            this._viewModel.OpenFilePickerRequested += this.ViewModel_OpenFilePickerRequested;
        }
    }
    private void Window_Opened(object? sender, EventArgs e)
    {
        this.DisplayPanel.Focus();

        // Enable drag-drop for file transfer
        DragDrop.SetAllowDrop(this.DisplayPanel, true);
        this.DisplayPanel.AddHandler(DragDrop.DropEvent, this.DisplayPanel_Drop);
        this.DisplayPanel.AddHandler(DragDrop.DragOverEvent, this.DisplayPanel_DragOver);
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
    #endregion

    #region ViewModel Event Handlers
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.FrameBitmap))
            this.FrameImage.InvalidateVisual();

        if (e.PropertyName == nameof(ViewerViewModel.DebugOverlayBitmap))
            this.DebugOverlayImage.InvalidateVisual();

        if (e.PropertyName == nameof(ViewerViewModel.IsFullscreen))
            this.UpdateFullscreenState();
    }
    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }
    private async void ViewModel_OpenFilePickerRequested(object? sender, EventArgs e)
    {
        if (this._viewModel is null)
            return;

        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select file to send"
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await this._viewModel.SendFileFromPathAsync(path);
        }
    }
    #endregion

    #region File Transfer Drag-Drop
    private void DisplayPanel_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }
    private async void DisplayPanel_Drop(object? sender, DragEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var files = e.Data.GetFiles();
        if (files is null)
            return;

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
            {
                await this._viewModel.SendFileFromPathAsync(path);
            }
        }
    }
    #endregion

    #region Display Panel Input Handling
    private async void DisplayPanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        var (x, y) = this.GetNormalizedPosition(e);
        if (x >= 0 && x <= 1 && y >= 0 && y <= 1)
        {
            await this._viewModel.SendMouseMoveAsync(x, y);
        }
    }
    private async void DisplayPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
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
        if (this._viewModel is not { IsInputEnabled: true })
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
        if (this._viewModel is not { IsInputEnabled: true })
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

        // Handle fullscreen toggle with F11 (always works, even with input disabled)
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            this._viewModel.ToggleFullscreenCommand.Execute(null);
            return;
        }

        // Handle exit fullscreen with ESC (always works, even with input disabled)
        if (e.Key == Key.Escape && this._viewModel.IsFullscreen)
        {
            e.Handled = true;
            this._viewModel.ToggleFullscreenCommand.Execute(null);
            return;
        }

        if (!this._viewModel.IsInputEnabled)
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.SendKeyDownAsync(keyCode, modifiers);
    }
    private async void DisplayPanel_KeyUp(object? sender, KeyEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.SendKeyUpAsync(keyCode, modifiers);
    }
    #endregion

    #region Fullscreen & Toolbar Management
    private DispatcherTimer? _toolbarHideTimer;
    private const int ToolbarHideDelayMs = 1500;
    private const int ToolbarTriggerZoneHeight = 50;

    private void UpdateFullscreenState()
    {
        if (this._viewModel?.IsFullscreen == true)
        {
            this.WindowState = WindowState.FullScreen;
            this.SystemDecorations = SystemDecorations.None;
        }
        else
        {
            this.WindowState = WindowState.Normal;
            this.SystemDecorations = SystemDecorations.Full;
        }
    }
    private void Window_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (this._viewModel is null || !this._viewModel.IsFullscreen)
            return;

        var position = e.GetPosition(this);

        if (position.Y <= ToolbarTriggerZoneHeight)
        {
            this._viewModel.IsToolbarVisible = true;
            this.ResetToolbarHideTimer();
        }
    }
    private void Toolbar_PointerEntered(object? sender, PointerEventArgs e)
    {
        this._toolbarHideTimer?.Stop();
    }
    private void Toolbar_PointerExited(object? sender, PointerEventArgs e)
    {
        if (this._viewModel?.IsFullscreen == true)
        {
            this.ResetToolbarHideTimer();
        }
    }
    private void ResetToolbarHideTimer()
    {
        this._toolbarHideTimer?.Stop();
        this._toolbarHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(ToolbarHideDelayMs)
        };
        this._toolbarHideTimer.Tick += (s, e) =>
        {
            this._toolbarHideTimer?.Stop();
            if (this._viewModel is { } vm)
                vm.IsToolbarVisible = false;
        };
        this._toolbarHideTimer.Start();
    }
    #endregion

    #region Helper Methods
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
    #endregion
}

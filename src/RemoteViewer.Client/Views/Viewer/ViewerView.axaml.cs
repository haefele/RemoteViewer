using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Win32.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.Viewer;
using RemoteViewer.Client.Common;
using System.ComponentModel;
using ProtocolMouseButton = RemoteViewer.Shared.Protocol.MouseButton;
using ProtocolKeyModifiers = RemoteViewer.Shared.Protocol.KeyModifiers;

namespace RemoteViewer.Client.Views.Viewer;

#pragma warning disable CA1001 // Disposal is handled via Window_Closed()
public partial class ViewerView : Window
{
    private readonly IWindowsKeyBlockerService _windowsKeyBlocker;

    private ViewerViewModel? _viewModel;
    private FrameCompositor? _frameCompositor;
    private IDisposable? _windowsKeyBlockerHandle;

    #region Constructor
    public ViewerView()
    {
        this.InitializeComponent();

        this._windowsKeyBlocker = App.Current.Services.GetRequiredService<IWindowsKeyBlockerService>();

        this.AddHandler(DragDrop.DragEnterEvent, this.Window_DragEnter);
        this.AddHandler(DragDrop.DragLeaveEvent, this.Window_DragLeave);
        this.AddHandler(DragDrop.DropEvent, this.Window_Drop);
    }
    #endregion

    #region Lifecycle Events
    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.PropertyChanged -= this.ViewModel_PropertyChanged;
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            this._viewModel.Connection.ViewerService?.FrameReady -= this.ViewerService_FrameReady;
            this._frameCompositor?.Dispose();
            this._frameCompositor = null;
        }

        this._viewModel = this.DataContext as ViewerViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.PropertyChanged += this.ViewModel_PropertyChanged;
            this._viewModel.Connection.RequiredViewerService.FrameReady += this.ViewerService_FrameReady;
            this._frameCompositor = new FrameCompositor();
        }
    }

    private void Window_Opened(object? sender, EventArgs e)
    {
        this.DisplayPanel.Focus();
        this._windowsKeyBlockerHandle = this._windowsKeyBlocker.StartBlocking(() => this.IsActive && (this._viewModel?.IsInputEnabled ?? false));
    }

    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.Connection.RequiredViewerService.ReleaseAllKeysAsync();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        this._windowsKeyBlockerHandle?.Dispose();

        if (this._viewModel is not null)
        {
            this._viewModel.PropertyChanged -= this.ViewModel_PropertyChanged;
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            this._viewModel.Connection.ViewerService?.FrameReady -= this.ViewerService_FrameReady;
            this._frameCompositor?.Dispose();
            this._frameCompositor = null;
            await this._viewModel.DisposeAsync();
        }
    }
    #endregion

    #region ViewModel Event Handlers
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerViewModel.IsFullscreen))
            this.UpdateFullscreenState();
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    private void ViewerService_FrameReady(object? sender, FrameReceivedEventArgs e)
    {
        var compositor = this._frameCompositor;
        if (compositor is null)
            return;

        try
        {
            if (e.Regions is [{ IsKeyframe: true }])
            {
                compositor.ApplyKeyframe(e.Regions, e.FrameNumber);
            }
            else
            {
                compositor.ApplyDeltaRegions(e.Regions, e.FrameNumber);
            }

            var frame = compositor.Canvas;
            var overlay = compositor.DebugOverlay;

            Dispatcher.UIThread.Post(() =>
            {
                if (this.FrameImage is { } frameImage)
                {
                    frameImage.Source = frame;
                    frameImage.InvalidateVisual();
                }

                if (this.DebugOverlayImage is { } overlayImage)
                {
                    overlayImage.Source = overlay;
                    overlayImage.IsVisible = overlay is not null;
                    overlayImage.InvalidateVisual();
                }
            });
        }
        catch (Exception ex)
        {
            var logger = App.Current.Services.GetRequiredService<ILogger<ViewerView>>();
            logger.LogError(ex, "Error processing frame");
        }
    }
    #endregion

    #region UI Event Handlers
    private async void DisplayMiniMap_DisplaySelected(object? sender, Shared.DisplayInfo display)
    {
        if (this._viewModel is null)
            return;

        this._viewModel.SelectDisplayCommand.Execute(display);

        await Task.Delay(50);

        if (sender is Control control)
        {
            var flyoutPresenter = control.FindAncestorOfType<FlyoutPresenter>();
            if (flyoutPresenter?.Parent is Popup popup)
            {
                popup.IsOpen = false;
            }
        }
    }
    #endregion

    #region Drag and Drop
    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.IsSingleFileDrag)
        {
            e.DragEffects = DragDropEffects.Copy;
            this.DropOverlay.IsVisible = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object? sender, DragEventArgs e)
    {
        this.DropOverlay.IsVisible = false;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        this.DropOverlay.IsVisible = false;

        if (this._viewModel is null)
            return;

        if (e.SingleFile?.TryGetLocalPath() is { } path && this._viewModel.SendFileCommand.CanExecute(path))
        {
            await this._viewModel.SendFileCommand.ExecuteAsync(path);
        }
    }
    #endregion

    #region Input Handling
    private async void DisplayPanel_KeyDown(object? sender, KeyEventArgs e)
    {
        if (this._viewModel is null)
            return;

        if (e.Key == Key.F11)
        {
            e.Handled = true;
            this._viewModel.ToggleFullscreenCommand.Execute(null);
            return;
        }

        if (e.Key == Key.Escape && this._viewModel.IsFullscreen)
        {
            e.Handled = true;
            this._viewModel.ToggleFullscreenCommand.Execute(null);
            return;
        }

        // Ctrl+Arrow keys for display navigation
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.Left && this._viewModel.NavigateLeftCommand.CanExecute(null))
            {
                e.Handled = true;
                this._viewModel.NavigateLeftCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Right && this._viewModel.NavigateRightCommand.CanExecute(null))
            {
                e.Handled = true;
                this._viewModel.NavigateRightCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Up && this._viewModel.NavigateUpCommand.CanExecute(null))
            {
                e.Handled = true;
                this._viewModel.NavigateUpCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Down && this._viewModel.NavigateDownCommand.CanExecute(null))
            {
                e.Handled = true;
                this._viewModel.NavigateDownCommand.Execute(null);
                return;
            }
        }

        if (!this._viewModel.IsInputEnabled)
            return;

        // Character-producing keys without shortcut modifiers will be handled by TextInput event
        if (IsCharacterProducingKey(e.Key) && !HasShortcutModifier(e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.Connection.RequiredViewerService.SendKeyDownAsync(keyCode, modifiers);
    }

    private async void DisplayPanel_TextInput(object? sender, TextInputEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true } || string.IsNullOrEmpty(e.Text))
            return;

        e.Handled = true;
        await this._viewModel.Connection.RequiredViewerService.SendTextInputAsync(e.Text);
    }

    private async void DisplayPanel_KeyUp(object? sender, KeyEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        // Character-producing keys without shortcut modifiers were handled by TextInput event
        if (IsCharacterProducingKey(e.Key) && !HasShortcutModifier(e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;

        var keyCode = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
        var modifiers = this.GetKeyModifiers(e.KeyModifiers);
        await this._viewModel.Connection.RequiredViewerService.SendKeyUpAsync(keyCode, modifiers);
    }

    private async void DisplayPanel_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            await this._viewModel.Connection.RequiredViewerService.SendMouseMoveAsync(x, y);
        }
    }

    private async void DisplayPanel_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            var point = e.GetCurrentPoint(this.FrameImage);
            var button = this.GetMouseButton(point.Properties);
            if (button is not null)
            {
                await this._viewModel.Connection.RequiredViewerService.SendMouseDownAsync(button.Value, x, y);
            }
        }
    }

    private async void DisplayPanel_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            var button = e.InitialPressMouseButton switch
            {
                MouseButton.Left => ProtocolMouseButton.Left,
                MouseButton.Right => ProtocolMouseButton.Right,
                MouseButton.Middle => ProtocolMouseButton.Middle,
                MouseButton.XButton1 => ProtocolMouseButton.XButton1,
                MouseButton.XButton2 => ProtocolMouseButton.XButton2,
                _ => (ProtocolMouseButton?)null
            };

            if (button is not null)
            {
                await this._viewModel.Connection.RequiredViewerService.SendMouseUpAsync(button.Value, x, y);
            }
        }
    }

    private async void DisplayPanel_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (this._viewModel is not { IsInputEnabled: true })
            return;

        if (this.TryGetNormalizedPosition(e, out var x, out var y))
        {
            await this._viewModel.Connection.RequiredViewerService.SendMouseWheelAsync((float)e.Delta.X, (float)e.Delta.Y, x, y);
        }
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
    private static bool IsCharacterProducingKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => true,
        >= Key.D0 and <= Key.D9 => true,
        >= Key.NumPad0 and <= Key.NumPad9 => true,
        Key.OemSemicolon or Key.OemPlus or Key.OemComma or Key.OemMinus or
        Key.OemPeriod or Key.OemQuestion or Key.OemTilde or Key.OemOpenBrackets or
        Key.OemPipe or Key.OemCloseBrackets or Key.OemQuotes or Key.OemBackslash => true,
        Key.Space or Key.Multiply or Key.Add or Key.Subtract or Key.Decimal or Key.Divide => true,
        _ => false
    };

    private static bool HasShortcutModifier(KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control) || modifiers.HasFlag(KeyModifiers.Alt);

    private bool TryGetNormalizedPosition(PointerEventArgs e, out float x, out float y)
    {
        x = -1;
        y = -1;

        var frame = this.FrameImage;
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
        { IsXButton1Pressed: true } => ProtocolMouseButton.XButton1,
        { IsXButton2Pressed: true } => ProtocolMouseButton.XButton2,
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

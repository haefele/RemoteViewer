using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Viewer;
using System.ComponentModel;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerView : Window, IDisposable
{
    private readonly IWindowsKeyBlockerService _windowsKeyBlocker;

    private ViewerViewModel? _viewModel;
    private ViewerAvaloniaConnectionAdapter? _connectionAdapter;
    private IDisposable? _windowsKeyBlockerHandle;

    #region Constructor
    public ViewerView()
    {
        this.InitializeComponent();

        this._windowsKeyBlocker = App.Current.Services.GetRequiredService<IWindowsKeyBlockerService>();
    }
    #endregion

    #region Lifecycle Events
    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this.UnbindConnectionAdapter();
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
            this.BindConnectionAdapter(this._viewModel);
        }
    }
    private void Window_Opened(object? sender, EventArgs e)
    {
        this.DisplayPanel.Focus();
        this._windowsKeyBlockerHandle = this._windowsKeyBlocker.StartBlocking(() => this.IsActive && (this._viewModel?.IsInputEnabled ?? false));
        this._windowsKeyBlocker.WindowsKeyDown += this.OnWindowsKeyDown;
        this._windowsKeyBlocker.WindowsKeyUp += this.OnWindowsKeyUp;
    }
    private async void Window_Deactivated(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.Connection.RequiredViewerService.ReleaseAllKeysAsync();
    }
    private async void Window_Closed(object? sender, EventArgs e)
    {
        this._windowsKeyBlocker.WindowsKeyDown -= this.OnWindowsKeyDown;
        this._windowsKeyBlocker.WindowsKeyUp -= this.OnWindowsKeyUp;
        this._windowsKeyBlockerHandle?.Dispose();

        if (this._viewModel is not null)
            this.UnbindConnectionAdapter();

        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
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

    private async void OnWindowsKeyDown(ushort vkCode)
    {
        if (this._viewModel is { IsInputEnabled: true })
            await this._viewModel.Connection.RequiredViewerService.SendWindowsKeyDownAsync();
    }
    private async void OnWindowsKeyUp(ushort vkCode)
    {
        if (this._viewModel is { IsInputEnabled: true })
            await this._viewModel.Connection.RequiredViewerService.SendWindowsKeyUpAsync();
    }

    private void DisplayPanel_KeyDown(object? sender, KeyEventArgs e)
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
        }
    }

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
    private void BindConnectionAdapter(ViewerViewModel viewModel)
    {
        var logger = App.Current.Services.GetRequiredService<ILogger<ViewerAvaloniaConnectionAdapter>>();
        this._connectionAdapter = new ViewerAvaloniaConnectionAdapter(viewModel.Connection, logger);
        this._connectionAdapter.Attach(this.DisplayPanel, this.FrameImage, this.DebugOverlayImage);
    }

    private void UnbindConnectionAdapter()
    {
        this._connectionAdapter?.Dispose();
        this._connectionAdapter = null;
    }
    #endregion

    public void Dispose()
    {
        this.UnbindConnectionAdapter();
        this._windowsKeyBlockerHandle?.Dispose();
        GC.SuppressFinalize(this);
    }
}

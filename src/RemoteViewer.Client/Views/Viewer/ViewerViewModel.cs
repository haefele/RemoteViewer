using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Server.SharedAPI.Protocol;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    #region Core State & Constructor
    private readonly Connection _connection;
    private readonly ILogger<ViewerViewModel> _logger;

    public ToastsViewModel Toasts { get; }

    [ObservableProperty]
    private string _title = "Remote Viewer";

    public event EventHandler? CloseRequested;
    public event EventHandler? OpenFilePickerRequested;

    public ViewerViewModel(
        Connection connection,
        IViewModelFactory viewModelFactory,
        ILogger<ViewerViewModel> logger)
    {
        this._connection = connection;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this._connection.FileTransfers.Toasts = this.Toasts;

        this._connection.ParticipantsChanged += this.Connection_ParticipantsChanged;
        this._connection.FrameReceived += this.Connection_FrameReceived;
        this._connection.Closed += this.Connection_Closed;
    }
    #endregion

    #region Frame Display
    private readonly FrameCompositor _compositor = new();
    private ulong _lastReceivedFrameNumber;

    [ObservableProperty]
    private WriteableBitmap? _frameBitmap;

    [ObservableProperty]
    private WriteableBitmap? _debugOverlayBitmap;

    private void Connection_FrameReceived(object? sender, FrameReceivedEventArgs e)
    {
        // Drop out-of-order delta frames (but always accept keyframes)
        if (e.Regions is not [{ IsKeyframe: true }] && e.FrameNumber <= this._lastReceivedFrameNumber)
            return;

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

            this._lastReceivedFrameNumber = e.FrameNumber;

            Dispatcher.UIThread.Post(() =>
            {
                if (this._disposed)
                    return;

                if (this._compositor.Canvas is { } canvas && this.FrameBitmap != canvas)
                {
                    this.FrameBitmap = canvas;
                }
                else
                {
                    this.OnPropertyChanged(nameof(this.FrameBitmap));
                }

                if (this._compositor.DebugOverlay is { } overlay && this.DebugOverlayBitmap != overlay)
                {
                    this.DebugOverlayBitmap = overlay;
                }
                else if (this._compositor.DebugOverlay is not null)
                {
                    this.OnPropertyChanged(nameof(this.DebugOverlayBitmap));
                }
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error processing frame");
        }
    }
    #endregion

    #region Session & Participants
    [ObservableProperty]
    private ObservableCollection<ParticipantDisplay> _participants = new();

    private void Connection_ParticipantsChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(this.UpdateParticipants);
    }

    private void UpdateParticipants()
    {
        var presenter = this._connection.Presenter;
        var viewers = this._connection.Viewers;

        this.Participants.Clear();

        if (presenter is not null)
        {
            this.Participants.Add(new ParticipantDisplay(presenter.DisplayName, IsPresenter: true));
        }

        foreach (var viewer in viewers)
        {
            this.Participants.Add(new ParticipantDisplay(viewer.DisplayName, IsPresenter: false));
        }
    }

    private void Connection_Closed(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => this.CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }
    #endregion

    #region Input Handling
    private readonly ConcurrentDictionary<ushort, object?> _pressedKeys = new();

    [ObservableProperty]
    private bool _isInputEnabled = true;

    [RelayCommand]
    private async Task ToggleInputAsync()
    {
        this.IsInputEnabled = !this.IsInputEnabled;
        if (!this.IsInputEnabled)
            await this.ReleaseAllKeysAsync();
    }

    public async Task SendMouseMoveAsync(float x, float y)
    {
        try
        {
            var message = new MouseMoveMessage(x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseMove, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse move");
        }
    }

    public async Task SendMouseDownAsync(MouseButton button, float x, float y)
    {
        try
        {
            var message = new MouseButtonMessage(button, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseDown, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse down");
        }
    }

    public async Task SendMouseUpAsync(MouseButton button, float x, float y)
    {
        try
        {
            var message = new MouseButtonMessage(button, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseUp, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse up");
        }
    }

    public async Task SendMouseWheelAsync(float deltaX, float deltaY, float x, float y)
    {
        try
        {
            var message = new MouseWheelMessage(deltaX, deltaY, x, y);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.MouseWheel, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send mouse wheel");
        }
    }

    public async Task SendKeyDownAsync(ushort keyCode, KeyModifiers modifiers)
    {
        try
        {
            this._pressedKeys.TryAdd(keyCode, null);

            var message = new KeyMessage(keyCode, modifiers);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.KeyDown, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key down");
        }
    }

    public async Task SendKeyUpAsync(ushort keyCode, KeyModifiers modifiers)
    {
        try
        {
            this._pressedKeys.TryRemove(keyCode, out _);

            var message = new KeyMessage(keyCode, modifiers);
            var data = ProtocolSerializer.Serialize(message);
            await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, data);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to send key up");
        }
    }

    public async Task ReleaseAllKeysAsync()
    {
        if (this._pressedKeys.IsEmpty)
            return;

        foreach (var keyCode in this._pressedKeys.Keys)
        {
            try
            {
                this._pressedKeys.TryRemove(keyCode, out _);

                var message = new KeyMessage(keyCode, KeyModifiers.None);
                var data = ProtocolSerializer.Serialize(message);
                await this._connection.SendInputAsync(MessageTypes.Input.KeyUp, data);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to release key");
            }
        }
    }
    #endregion

    #region Fullscreen & Toolbar
    [ObservableProperty]
    private bool _isFullscreen;

    [ObservableProperty]
    private bool _isToolbarVisible;

    [RelayCommand]
    private void ToggleFullscreen()
    {
        this.IsFullscreen = !this.IsFullscreen;
        if (this.IsFullscreen)
            this.IsToolbarVisible = false;
    }

    [RelayCommand]
    private async Task NextDisplay()
    {
        this.Toasts.Info("Switching display...");
        await this._connection.SwitchDisplayAsync();
    }
    #endregion

    #region File Transfer
    public FileTransferService FileTransfers => this._connection.FileTransfers;

    [RelayCommand]
    private void SendFile()
    {
        this.OpenFilePickerRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task SendFileFromPathAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                this.Toasts.Error($"File not found: {filePath}");
                return;
            }

            var transfer = await this._connection.FileTransfers.SendFileAsync(filePath);
            this.Toasts.AddTransfer(transfer, isUpload: true);
            this._logger.LogInformation("Started file upload: {FileName} ({FileSize} bytes)", transfer.FileName, transfer.FileSize);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to initiate file upload for {FilePath}", filePath);
            this.Toasts.Error($"Failed to send file: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CancelTransfer(IFileTransfer transfer)
    {
        await transfer.CancelAsync();
    }
    #endregion

    #region Cleanup
    private bool _disposed;

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        await this._connection.FileTransfers.CancelAllAsync();
        await this._connection.DisconnectAsync();

        this._connection.ParticipantsChanged -= this.Connection_ParticipantsChanged;
        this._connection.FrameReceived -= this.Connection_FrameReceived;
        this._connection.Closed -= this.Connection_Closed;

        this._compositor.Dispose();

        GC.SuppressFinalize(this);
    }
    #endregion
}

public record ParticipantDisplay(string DisplayName, bool IsPresenter);

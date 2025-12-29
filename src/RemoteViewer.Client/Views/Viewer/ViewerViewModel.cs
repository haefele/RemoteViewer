using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using System.Collections.ObjectModel;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    #region Core State & Constructor
    public Connection Connection { get; }
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
        this.Connection = connection;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this.Connection.FileTransfers.Toasts = this.Toasts;

        this.Connection.ParticipantsChanged += this.Connection_ParticipantsChanged;
        this.Connection.Closed += this.Connection_Closed;
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
        var presenter = this.Connection.Presenter;
        var viewers = this.Connection.Viewers;

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
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this.Connection.ConnectionId);
        await this.Connection.DisconnectAsync();
    }
    #endregion

    #region Input Handling
    public bool IsInputEnabled
    {
        get => this.Connection.RequiredViewerService.IsInputEnabled;
        set
        {
            var service = this.Connection.RequiredViewerService;
            if (service.IsInputEnabled == value)
                return;

            service.IsInputEnabled = value;
            this.OnPropertyChanged();
        }
    }

    [RelayCommand]
    private async Task ToggleInputAsync()
    {
        this.IsInputEnabled = !this.IsInputEnabled;
        if (!this.IsInputEnabled)
            await this.Connection.RequiredViewerService.ReleaseAllKeysAsync();
    }

    [RelayCommand]
    private Task SendCtrlAltDelAsync() =>
        this.Connection.RequiredViewerService.SendCtrlAltDelAsync();
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
        await this.Connection.RequiredViewerService.SwitchDisplayAsync();
    }
    #endregion

    #region File Transfer
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

            var transfer = await this.Connection.FileTransfers.SendFileAsync(filePath);
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

        await this.Connection.FileTransfers.CancelAllAsync();
        await this.Connection.DisconnectAsync();

        this.Connection.ParticipantsChanged -= this.Connection_ParticipantsChanged;
        this.Connection.Closed -= this.Connection_Closed;
        this.Connection.ViewerService?.Dispose();

        GC.SuppressFinalize(this);
    }
    #endregion
}

public record ParticipantDisplay(string DisplayName, bool IsPresenter);

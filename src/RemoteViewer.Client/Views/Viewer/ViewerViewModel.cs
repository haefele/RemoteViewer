using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Views.Viewer;

public partial class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    #region Core State & Constructor
    public Connection Connection { get; }
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<ViewerViewModel> _logger;

    public ToastsViewModel Toasts { get; }
    public ChatViewModel Chat { get; }

    public event EventHandler? CloseRequested;

    public ViewerViewModel(
        Connection connection,
        IDispatcher dispatcher,
        IViewModelFactory viewModelFactory,
        ILoggerFactory loggerFactory)
    {
        this.Connection = connection;
        this._dispatcher = dispatcher;
        this._logger = loggerFactory.CreateLogger<ViewerViewModel>();
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this.Chat = new ChatViewModel(this.Connection.Chat, dispatcher, loggerFactory.CreateLogger<ChatViewModel>());
        this.Connection.FileTransfers.Toasts = this.Toasts;

        this.Connection.ParticipantsChanged += this.Connection_ParticipantsChanged;
        this.Connection.ConnectionPropertiesChanged += this.Connection_ConnectionPropertiesChanged;
        this.Connection.Closed += this.Connection_Closed;

        var viewerService = this.Connection.RequiredViewerService;
        viewerService.AvailableDisplaysChanged += this.ViewerService_AvailableDisplaysChanged;
        viewerService.CurrentDisplayChanged += this.ViewerService_CurrentDisplayChanged;
    }
    #endregion

    #region Session & Participants
    [ObservableProperty]
    private ObservableCollection<ParticipantDisplay> _participants = new();

    private void Connection_ParticipantsChanged(object? sender, EventArgs e)
    {
        this._dispatcher.Post(this.UpdateParticipants);
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

    private void Connection_ConnectionPropertiesChanged(object? sender, EventArgs e)
    {
        this._dispatcher.Post(this.UpdateConnectionProperties);
    }

    private void UpdateConnectionProperties()
    {
        var clientId = this.Connection.Owner.ClientId;
        var blockedIds = this.Connection.ConnectionProperties.InputBlockedViewerIds;
        var isBlocked = clientId is not null && blockedIds.Contains(clientId);
        this.IsInputBlockedByPresenter = isBlocked;
        this.CanSendSecureAttentionSequence = this.Connection.ConnectionProperties.CanSendSecureAttentionSequence;
    }

    private void Connection_Closed(object? sender, EventArgs e)
    {
        this._dispatcher.Post(() => this.CloseRequested?.Invoke(this, EventArgs.Empty));
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        this._logger.LogInformation("User requested to disconnect from connection {ConnectionId}", this.Connection.ConnectionId);
        await this.Connection.DisconnectAsync();
    }
    #endregion

    #region Input Handling
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleInputCommand))]
    [NotifyCanExecuteChangedFor(nameof(SendCtrlAltDelCommand))]
    private bool _isInputBlockedByPresenter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCtrlAltDelCommand))]
    private bool _canSendSecureAttentionSequence;

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

    private bool CanToggleInput() => !this.IsInputBlockedByPresenter;
    [RelayCommand(CanExecute = nameof(CanToggleInput))]
    private async Task ToggleInputAsync()
    {
        this.IsInputEnabled = !this.IsInputEnabled;

        if (!this.IsInputEnabled)
            await this.Connection.RequiredViewerService.ReleaseAllKeysAsync();
    }

    private bool CanSendCtrlAltDel() => this.IsInputBlockedByPresenter is false && this.CanSendSecureAttentionSequence;
    [RelayCommand(CanExecute = nameof(CanSendCtrlAltDel))]
    private Task SendCtrlAltDelAsync()
    {
        return this.Connection.RequiredViewerService.SendCtrlAltDelAsync();
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
    #endregion

    #region Display Navigation
    [ObservableProperty]
    private ImmutableList<DisplayInfo> _availableDisplays = [];

    [ObservableProperty]
    private string? _currentDisplayId;

    [ObservableProperty]
    private ImmutableList<DisplayInfo> _leftAdjacentDisplays = [];

    [ObservableProperty]
    private ImmutableList<DisplayInfo> _rightAdjacentDisplays = [];

    [ObservableProperty]
    private ImmutableList<DisplayInfo> _upAdjacentDisplays = [];

    [ObservableProperty]
    private ImmutableList<DisplayInfo> _downAdjacentDisplays = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateLeftCommand))]
    private bool _canNavigateLeft;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateRightCommand))]
    private bool _canNavigateRight;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateUpCommand))]
    private bool _canNavigateUp;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NavigateDownCommand))]
    private bool _canNavigateDown;

    private void ViewerService_AvailableDisplaysChanged(object? sender, EventArgs e)
    {
        this._dispatcher.Post(this.UpdateDisplayState);
    }

    private void ViewerService_CurrentDisplayChanged(object? sender, EventArgs e)
    {
        this._dispatcher.Post(this.UpdateDisplayState);
    }

    private void UpdateDisplayState()
    {
        var viewerService = this.Connection.RequiredViewerService;
        this.AvailableDisplays = viewerService.AvailableDisplays;
        this.CurrentDisplayId = viewerService.CurrentDisplay?.Id;

        this.LeftAdjacentDisplays = viewerService.GetAdjacentDisplays(NavigationDirection.Left);
        this.RightAdjacentDisplays = viewerService.GetAdjacentDisplays(NavigationDirection.Right);
        this.UpAdjacentDisplays = viewerService.GetAdjacentDisplays(NavigationDirection.Up);
        this.DownAdjacentDisplays = viewerService.GetAdjacentDisplays(NavigationDirection.Down);

        this.CanNavigateLeft = this.LeftAdjacentDisplays.Count > 0;
        this.CanNavigateRight = this.RightAdjacentDisplays.Count > 0;
        this.CanNavigateUp = this.UpAdjacentDisplays.Count > 0;
        this.CanNavigateDown = this.DownAdjacentDisplays.Count > 0;
    }

    private bool CanNavigateLeftExecute() => this.CanNavigateLeft;
    [RelayCommand(CanExecute = nameof(CanNavigateLeftExecute))]
    private async Task NavigateLeft()
    {
        var leftDisplay = this.LeftAdjacentDisplays.FirstOrDefault();
        if (leftDisplay is not null)
            await this.Connection.RequiredViewerService.SelectDisplayAsync(leftDisplay.Id);
    }

    private bool CanNavigateRightExecute() => this.CanNavigateRight;
    [RelayCommand(CanExecute = nameof(CanNavigateRightExecute))]
    private async Task NavigateRight()
    {
        var rightDisplay = this.RightAdjacentDisplays.FirstOrDefault();
        if (rightDisplay is not null)
            await this.Connection.RequiredViewerService.SelectDisplayAsync(rightDisplay.Id);
    }

    private bool CanNavigateUpExecute() => this.CanNavigateUp;
    [RelayCommand(CanExecute = nameof(CanNavigateUpExecute))]
    private async Task NavigateUp()
    {
        var upDisplay = this.UpAdjacentDisplays.FirstOrDefault();
        if (upDisplay is not null)
            await this.Connection.RequiredViewerService.SelectDisplayAsync(upDisplay.Id);
    }

    private bool CanNavigateDownExecute() => this.CanNavigateDown;
    [RelayCommand(CanExecute = nameof(CanNavigateDownExecute))]
    private async Task NavigateDown()
    {
        var downDisplay = this.DownAdjacentDisplays.FirstOrDefault();
        if (downDisplay is not null)
            await this.Connection.RequiredViewerService.SelectDisplayAsync(downDisplay.Id);
    }

    [RelayCommand]
    private async Task SelectDisplay(DisplayInfo display)
    {
        await this.Connection.RequiredViewerService.SelectDisplayAsync(display.Id);
    }
    #endregion

    #region File Transfer
    [RelayCommand]
    public async Task SendFile(string? path = null)
    {
        try
        {
            // If no path is provided, open file picker
            if (path is null)
            {
                var files = await App.Current.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    AllowMultiple = false,
                    Title = "Select file to send"
                });

                if (files.Count == 0 || files[0].TryGetLocalPath() is not { } filePath)
                    return;

                path = filePath;
            }

            // Validate file existence
            if (File.Exists(path) is false)
            {
                this.Toasts.Error($"File not found: {path}");
                return;
            }

            // Start file transfer
            var transfer = await this.Connection.FileTransfers.SendFileAsync(path);
            this.Toasts.AddTransfer(transfer, isUpload: true);
            this._logger.LogInformation("Started file upload: {FileName} ({FileSize} bytes)", transfer.FileName, transfer.FileSize);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to initiate file upload for {FilePath}", path);
            this.Toasts.Error($"Failed to send file: {ex.Message}");
        }
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

        this.Chat.Dispose();

        this.Connection.ParticipantsChanged -= this.Connection_ParticipantsChanged;
        this.Connection.ConnectionPropertiesChanged -= this.Connection_ConnectionPropertiesChanged;
        this.Connection.Closed -= this.Connection_Closed;

        var viewerService = this.Connection.ViewerService;
        if (viewerService is not null)
        {
            viewerService.AvailableDisplaysChanged -= this.ViewerService_AvailableDisplaysChanged;
            viewerService.CurrentDisplayChanged -= this.ViewerService_CurrentDisplayChanged;
        }

        GC.SuppressFinalize(this);
    }
    #endregion
}

public record ParticipantDisplay(string DisplayName, bool IsPresenter);

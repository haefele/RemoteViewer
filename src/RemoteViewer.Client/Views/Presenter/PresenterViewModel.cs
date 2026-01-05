using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Platform.Storage;
using DispatcherTimer = Avalonia.Threading.DispatcherTimer;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Dialogs;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ConnectionHubClient _hubClient;
    private readonly IFrameEncoder _frameEncoder;
    private readonly IDisplayService _displayService;
    private readonly IDialogService _dialogService;
    private readonly IDispatcher _dispatcher;
    private readonly ILogger<PresenterViewModel> _logger;
    private readonly DispatcherTimer _bandwidthTimer;

    private (int Width, int Height)? _biggestScreen;

    public ToastsViewModel Toasts { get; }
    public ChatViewModel Chat { get; }

    private bool _disposed;

    [ObservableProperty]
    private string? _yourId;

    [ObservableProperty]
    private string? _yourPassword;

    public ObservableCollection<PresenterViewerDisplay> Viewers { get; } = [];

    public int TargetFps
    {
        get => this._connection.PresenterCapture?.TargetFps ?? 15;
        set
        {
            if (this._connection.PresenterCapture is { } capture && capture.TargetFps != value)
            {
                capture.TargetFps = value;
                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(this.EstimatedBandwidth));
            }
        }
    }

    public int Quality
    {
        get => this._frameEncoder.Quality;
        set
        {
            if (this._frameEncoder.Quality != value)
            {
                this._frameEncoder.Quality = value;
                this.OnPropertyChanged();
                this.OnPropertyChanged(nameof(this.EstimatedBandwidth));
            }
        }
    }

    public string EstimatedBandwidth
    {
        get
        {
            var (width, height) = this._biggestScreen ?? (1920, 1080);
            return BandwidthEstimator.Calculate(width, height, this.TargetFps, this.Quality);
        }
    }

    public string ActualBandwidth => this._connection.BandwidthTracker.GetFormatted();

    public event EventHandler? CloseRequested;

    public PresenterViewModel(
        Connection connection,
        ConnectionHubClient hubClient,
        IFrameEncoder frameEncoder,
        IDisplayService displayService,
        IDialogService dialogService,
        IDispatcher dispatcher,
        IViewModelFactory viewModelFactory,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._hubClient = hubClient;
        this._frameEncoder = frameEncoder;
        this._displayService = displayService;
        this._dialogService = dialogService;
        this._dispatcher = dispatcher;
        this._logger = loggerFactory.CreateLogger<PresenterViewModel>();

        this._bandwidthTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        this._bandwidthTimer.Tick += (_, _) => this.OnPropertyChanged(nameof(this.ActualBandwidth));
        this._bandwidthTimer.Start();

        _ = this.UpdateBiggestScreenAsync();
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this.Chat = new ChatViewModel(this._connection.Chat, dispatcher, loggerFactory.CreateLogger<ChatViewModel>());
        this._connection.FileTransfers.Toasts = this.Toasts;

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.ConnectionPropertiesChanged += this.OnConnectionPropertiesChanged;
        this._connection.Closed += this.OnConnectionClosed;

        // Subscribe to credentials changes
        this._hubClient.CredentialsAssigned += this.OnCredentialsAssigned;
    }

    private async Task UpdateBiggestScreenAsync()
    {
        var displays = await this._displayService.GetDisplays(this._connection.ConnectionId, CancellationToken.None);
        var largest = displays.MaxBy(d => d.Width * d.Height);
        if (largest is not null)
        {
            this._biggestScreen = (largest.Width, largest.Height);
            this.OnPropertyChanged(nameof(this.EstimatedBandwidth));
        }
    }

    private void OnCredentialsAssigned(object? sender, CredentialsAssignedEventArgs e)
    {
        this._dispatcher.Post(() =>
        {
            this.YourId = e.Username;
            this.YourPassword = e.Password;
        });
    }

    private void OnViewersChanged(object? sender, EventArgs e)
    {
        var viewers = this._connection.Viewers;

        // Log new/removed viewers for debugging
        this._logger.LogInformation("Viewer list changed: {ViewerCount} viewer(s)", viewers.Count);

        // Update UI
        this._dispatcher.Post(() =>
        {
            this.UpdateViewers(viewers);
        });
    }

    private void OnConnectionPropertiesChanged(object? sender, EventArgs e)
    {
        this._dispatcher.Post(this.UpdateBlockedViewerStates);
    }

    private void UpdateViewers(IReadOnlyList<ClientInfo> viewers)
    {
        // Build set of current viewer IDs
        var currentViewerIds = viewers.Select(v => v.ClientId).ToHashSet();

        // Remove viewers that are no longer connected
        for (var i = this.Viewers.Count - 1; i >= 0; i--)
        {
            if (!currentViewerIds.Contains(this.Viewers[i].ClientId))
            {
                this.Viewers[i].PropertyChanged -= this.Viewer_PropertyChanged;
                this.Viewers.RemoveAt(i);
            }
        }

        // Add new viewers
        var existingIds = this.Viewers.Select(p => p.ClientId).ToHashSet();
        foreach (var viewer in viewers)
        {
            if (!existingIds.Contains(viewer.ClientId))
            {
                var display = new PresenterViewerDisplay(
                    viewer.ClientId,
                    viewer.DisplayName)
                {
                    IsInputBlocked = this._connection.ConnectionProperties.InputBlockedViewerIds.Contains(viewer.ClientId)
                };
                display.PropertyChanged += this.Viewer_PropertyChanged;
                this.Viewers.Add(display);
            }
        }
    }

    private void UpdateBlockedViewerStates()
    {
        var blockedIds = this._connection.ConnectionProperties.InputBlockedViewerIds.ToHashSet(StringComparer.Ordinal);
        foreach (var viewer in this.Viewers)
        {
            var isBlocked = blockedIds.Contains(viewer.ClientId);
            if (viewer.IsInputBlocked != isBlocked)
            {
                viewer.IsInputBlocked = isBlocked;
            }
        }
    }

    private void Viewer_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PresenterViewerDisplay.IsInputBlocked))
            return;

        if (sender is PresenterViewerDisplay viewer)
        {
            _ = this._connection.UpdateConnectionPropertiesAndSend(current =>
            {
                var blockedIds = current.InputBlockedViewerIds.ToHashSet(StringComparer.Ordinal);

                if (viewer.IsInputBlocked)
                {
                    blockedIds.Add(viewer.ClientId);
                }
                else
                {
                    blockedIds.Remove(viewer.ClientId);
                }

                return current with { InputBlockedViewerIds = blockedIds.ToList() };
            });
        }
    }

    private void OnConnectionClosed(object? sender, EventArgs e)
    {
        this._dispatcher.Post(() =>
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }
    [RelayCommand]
    private async Task StopPresentingAsync()
    {
        this._logger.LogInformation("User requested to stop presenting for connection {ConnectionId}", this._connection.ConnectionId);
        await this._connection.DisconnectAsync();
    }

    [RelayCommand]
    private async Task CopyCredentials()
    {
        if (this.YourId is null || this.YourPassword is null)
            return;

        var text = $"""
                    ID: {this.YourId}
                    Password: {this.YourPassword}
                    """;
        var clipboard = App.Current.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
        this.Toasts.Success("ID and password copied to clipboard.");
    }

    [RelayCommand]
    private async Task GenerateNewPasswordAsync()
    {
        await this._hubClient.GenerateNewPassword();
    }


    [RelayCommand]
    public async Task SendFile(string? path = null)
    {
        // Check for connected viewers
        if (this.Viewers.Count == 0)
        {
            this.Toasts.Error("No viewers connected to receive the file.");
            return;
        }

        // If no path provided, open file picker
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
        if (!File.Exists(path))
        {
            this.Toasts.Error($"File not found: {path}");
            return;
        }

        // Select viewers
        IReadOnlyList<string> viewerIds;
        if (this.Viewers.Count == 1)
        {
            viewerIds = [this.Viewers[0].ClientId];
        }
        else
        {
            var fileInfo = new FileInfo(path);
            var selected = await this._dialogService.ShowViewerSelectionAsync(
                this.Viewers,
                fileInfo.Name,
                FileTransferHelpers.FormatFileSize(fileInfo.Length));

            if (selected is null or { Count: 0 })
                return;

            viewerIds = selected;
        }

        // Start file transfer
        foreach (var viewerId in viewerIds)
        {
            try
            {
                var transfer = await this._connection.FileTransfers.SendFileToViewerAsync(path, viewerId);
                this.Toasts.AddTransfer(transfer, isUpload: true);
                var viewer = this.Viewers.FirstOrDefault(v => v.ClientId == viewerId);
                this._logger.LogInformation("Started file send to {ViewerName}: {FileName} ({FileSize} bytes)",
                    viewer?.DisplayName ?? viewerId, transfer.FileName, transfer.FileSize);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to initiate file send to {ViewerId} for {FilePath}", viewerId, path);
                this.Toasts.Error($"Failed to send file: {ex.Message}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._bandwidthTimer.Stop();

        await this._connection.FileTransfers.CancelAllAsync();
        await this._connection.DisconnectAsync();

        this.Chat.Dispose();

        // Unsubscribe from Connection events
        this._connection.ViewersChanged -= this.OnViewersChanged;
        this._connection.ConnectionPropertiesChanged -= this.OnConnectionPropertiesChanged;
        this._connection.Closed -= this.OnConnectionClosed;
        this._hubClient.CredentialsAssigned -= this.OnCredentialsAssigned;
        foreach (var viewer in this.Viewers)
        {
            viewer.PropertyChanged -= this.Viewer_PropertyChanged;
        }
        GC.SuppressFinalize(this);
    }
}


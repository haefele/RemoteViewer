using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.FileTransfer;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Connection _connection;
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<PresenterViewModel> _logger;

    public ToastsViewModel Toasts { get; }
    public ChatViewModel Chat { get; }

    private bool _disposed;

    [ObservableProperty]
    private string? _yourId;

    [ObservableProperty]
    private string? _yourPassword;

    public ObservableCollection<PresenterViewerDisplay> Viewers { get; } = [];

    public event EventHandler? CloseRequested;

    public PresenterViewModel(
        Connection connection,
        ConnectionHubClient hubClient,
        IViewModelFactory viewModelFactory,
        ILoggerFactory loggerFactory)
    {
        this._connection = connection;
        this._hubClient = hubClient;
        this._logger = loggerFactory.CreateLogger<PresenterViewModel>();
        this.Toasts = viewModelFactory.CreateToastsViewModel();
        this.Chat = new ChatViewModel(this._connection.Chat, loggerFactory.CreateLogger<ChatViewModel>());
        this._connection.FileTransfers.Toasts = this.Toasts;

        // Subscribe to Connection events
        this._connection.ViewersChanged += this.OnViewersChanged;
        this._connection.ConnectionPropertiesChanged += this.OnConnectionPropertiesChanged;
        this._connection.Closed += this.OnConnectionClosed;

        // Subscribe to credentials changes
        this._hubClient.CredentialsAssigned += this.OnCredentialsAssigned;
    }

    private void OnCredentialsAssigned(object? sender, CredentialsAssignedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
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
        Dispatcher.UIThread.Post(() =>
        {
            this.UpdateViewers(viewers);
        });
    }

    private void OnConnectionPropertiesChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(this.UpdateBlockedViewerStates);
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
        Dispatcher.UIThread.Post(() =>
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
            var dialog = ViewerSelectionDialog.Create(
                this.Viewers,
                fileInfo.Name,
                FileTransferHelpers.FormatFileSize(fileInfo.Length));

            var selected = await dialog.ShowDialog<IReadOnlyList<string>?>(App.Current.ActiveWindow);
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


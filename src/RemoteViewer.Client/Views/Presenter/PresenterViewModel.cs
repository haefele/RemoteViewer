using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.Views.Presenter;

/// <summary>
/// ViewModel for the presenter window that shows presentation status.
/// This is a thin UI layer - the actual screen capture logic is in PresenterService.
/// </summary>
public partial class PresenterViewModel : ViewModelBase, IDisposable
{
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<PresenterViewModel> _logger;
    private readonly string _connectionId;
    private bool _disposed;

    [ObservableProperty]
    private string _title = "Presenting";

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private bool _isPresenting = true;

    [ObservableProperty]
    private string _statusText = "Waiting for viewers...";

    public event EventHandler? CloseRequested;

    public PresenterViewModel(
        ConnectionHubClient hubClient,
        string connectionId,
        ILogger<PresenterViewModel> logger)
    {
        _hubClient = hubClient;
        _connectionId = connectionId;
        _logger = logger;

        Title = $"Presenting - {connectionId[..8]}...";

        // Listen for connection changes to update viewer count
        _hubClient.ConnectionChanged += OnConnectionChanged;
        _hubClient.ConnectionStopped += OnConnectionStopped;
    }

    private void OnConnectionChanged(object? sender, ConnectionChangedEventArgs e)
    {
        if (e.ConnectionInfo.ConnectionId != _connectionId)
            return;

        // Only update if we're the presenter
        if (_hubClient.ClientId != e.ConnectionInfo.PresenterClientId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            ViewerCount = e.ConnectionInfo.ViewerClientIds.Count;
            StatusText = ViewerCount == 0
                ? "Waiting for viewers..."
                : $"{ViewerCount} viewer(s) connected";
        });
    }

    private void OnConnectionStopped(object? sender, ConnectionStoppedEventArgs e)
    {
        if (e.ConnectionId != _connectionId)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            IsPresenting = false;
            StatusText = "Connection closed";
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private async Task StopPresenting()
    {
        _logger.LogInformation("User requested to stop presenting for connection {ConnectionId}", _connectionId);
        await _hubClient.Disconnect(_connectionId);
        // The ConnectionStopped event will trigger CloseRequested
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _hubClient.ConnectionChanged -= OnConnectionChanged;
        _hubClient.ConnectionStopped -= OnConnectionStopped;

        GC.SuppressFinalize(this);
    }
}

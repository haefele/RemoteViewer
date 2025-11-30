using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Views;

namespace RemoteViewer.Client.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<ViewerWindowViewModel> _viewerLogger;

    [ObservableProperty]
    private string _yourUsername = "Connecting...";

    [ObservableProperty]
    private string _yourPassword = "...";

    [ObservableProperty]
    private string _targetUsername = "";

    [ObservableProperty]
    private string _targetPassword = "";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    private string? _connectionError;

    [ObservableProperty]
    private bool _isConnecting;

    public MainWindowViewModel(ConnectionHubClient hubClient, ILogger<ViewerWindowViewModel> viewerLogger)
    {
        _hubClient = hubClient;
        _viewerLogger = viewerLogger;

        _hubClient.CredentialsAssigned += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                YourUsername = e.Username;
                YourPassword = e.Password;
                IsConnected = true;
                StatusText = "Connected to server";
                ConnectionError = null;
            });
        };

        _hubClient.Closed += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = false;
                StatusText = "Disconnected";
                YourUsername = "Reconnecting...";
                YourPassword = "...";
            });
        };

        _hubClient.Reconnecting += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = false;
                StatusText = "Reconnecting...";
            });
        };

        _hubClient.Reconnected += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsConnected = true;
                StatusText = "Connected to server";
            });
        };

        // Handle viewer connections - open viewer window when connected as viewer
        _hubClient.ConnectionStarted += OnConnectionStarted;
    }

    private void OnConnectionStarted(object? sender, ConnectionStartedEventArgs e)
    {
        // Only open viewer window if we're the viewer (not presenter)
        if (!e.IsPresenter)
        {
            Dispatcher.UIThread.Post(() =>
            {
                OpenViewerWindow(e.ConnectionId);
            });
        }
    }

    private void OpenViewerWindow(string connectionId)
    {
        var viewModel = new ViewerWindowViewModel(_hubClient, connectionId, _viewerLogger);
        var window = new ViewerWindow
        {
            DataContext = viewModel
        };
        window.Show();
    }

    public async Task InitializeAsync()
    {
        await _hubClient.StartAsync();
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(TargetUsername) || string.IsNullOrWhiteSpace(TargetPassword))
        {
            ConnectionError = "Username and password are required";
            return;
        }

        IsConnecting = true;
        ConnectionError = null;

        try
        {
            var error = await _hubClient.ConnectTo(TargetUsername, TargetPassword);

            if (error is null)
            {
                ConnectionError = null;
                // Connection successful - will receive ConnectionStarted event
            }
            else
            {
                ConnectionError = error switch
                {
                    Server.SharedAPI.TryConnectError.IncorrectUsernameOrPassword => "Incorrect username or password",
                    Server.SharedAPI.TryConnectError.ViewerNotFound => "Connection error - please try again",
                    Server.SharedAPI.TryConnectError.CannotConnectToYourself => "Cannot connect to yourself",
                    _ => "Unknown error occurred"
                };
            }
        }
        finally
        {
            IsConnecting = false;
        }
    }
}

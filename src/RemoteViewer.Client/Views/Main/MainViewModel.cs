using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Views.Viewer;
#if WINDOWS
using RemoteViewer.Client.Views.Presenter;
#endif

namespace RemoteViewer.Client.Views.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<ViewerViewModel> _viewerLogger;
#if WINDOWS
    private readonly ILogger<PresenterViewModel> _presenterLogger;
#endif

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

    // Events for MainView visibility
    public event EventHandler? RequestHideMainView;
    public event EventHandler? RequestShowMainView;

#if WINDOWS
    public MainViewModel(ConnectionHubClient hubClient, ILogger<ViewerViewModel> viewerLogger, ILogger<PresenterViewModel> presenterLogger)
    {
        _hubClient = hubClient;
        _viewerLogger = viewerLogger;
        _presenterLogger = presenterLogger;
#else
    public MainViewModel(ConnectionHubClient hubClient, ILogger<ViewerViewModel> viewerLogger)
    {
        _hubClient = hubClient;
        _viewerLogger = viewerLogger;
#endif

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
        Dispatcher.UIThread.Post(() =>
        {
            if (e.IsPresenter)
            {
#if WINDOWS
                OpenPresenterWindow(e.ConnectionId);
#endif
            }
            else
            {
                OpenViewerWindow(e.ConnectionId);
            }
        });
    }

#if WINDOWS
    private void OpenPresenterWindow(string connectionId)
    {
        RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var viewModel = new PresenterViewModel(_hubClient, connectionId, _presenterLogger);
        var window = new PresenterView
        {
            DataContext = viewModel
        };

        window.Closed += OnSessionWindowClosed;
        window.Show();
    }
#endif

    private void OpenViewerWindow(string connectionId)
    {
        RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var viewModel = new ViewerViewModel(_hubClient, connectionId, _viewerLogger);
        var window = new ViewerView
        {
            DataContext = viewModel
        };

        window.Closed += OnSessionWindowClosed;
        window.Show();
    }

    private async void OnSessionWindowClosed(object? sender, EventArgs e)
    {
        // Reconnect to get fresh credentials
        await _hubClient.ReconnectAsync();
        // Show MainView again
        RequestShowMainView?.Invoke(this, EventArgs.Empty);
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

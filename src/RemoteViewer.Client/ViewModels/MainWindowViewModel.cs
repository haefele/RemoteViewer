using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;

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

    public MainWindowViewModel(ConnectionHubClient hubClient)
    {
        _hubClient = hubClient;

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

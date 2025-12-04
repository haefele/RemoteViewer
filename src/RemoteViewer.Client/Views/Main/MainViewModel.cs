using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;

namespace RemoteViewer.Client.Views.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly ILogger<ViewerViewModel> _viewerLogger;
    private readonly ILogger<PresenterViewModel> _presenterLogger;
    private readonly IScreenshotService _screenshotService;
    private readonly IInputInjectionService _inputInjectionService;

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

    public MainViewModel(
        ConnectionHubClient hubClient,
        ILogger<ViewerViewModel> viewerLogger,
        ILogger<PresenterViewModel> presenterLogger,
        IScreenshotService screenshotService,
        IInputInjectionService inputInjectionService)
    {
        this._hubClient = hubClient;
        this._viewerLogger = viewerLogger;
        this._presenterLogger = presenterLogger;
        this._screenshotService = screenshotService;
        this._inputInjectionService = inputInjectionService;

        this._hubClient.HubDisconnected += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.IsConnected = false;
                this.StatusText = "Reconnecting...";
                this.YourUsername = "...";
                this.YourPassword = "...";
            });
        };

        this._hubClient.HubConnected += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.IsConnected = true;
                this.StatusText = "Connected to server";
            });
        };

        this._hubClient.CredentialsAssigned += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.YourUsername = e.Username;
                this.YourPassword = e.Password;
                this.ConnectionError = null;
            });
        };

        this.IsConnected = this._hubClient.IsConnected;

        // Handle viewer connections - open viewer window when connected as viewer
        this._hubClient.ConnectionStarted += this.OnConnectionStarted;
    }

    private void OnConnectionStarted(object? sender, ConnectionStartedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Connection.Role == ConnectionRole.Presenter)
            {
                this.OpenPresenterWindow(e.Connection);
            }
            else
            {
                this.OpenViewerWindow(e.Connection);
            }
        });
    }

    private void OpenPresenterWindow(Connection connection)
    {
        RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var viewModel = new PresenterViewModel(
            connection,
            this._screenshotService,
            this._inputInjectionService,
            this._presenterLogger);
        var window = new PresenterView
        {
            DataContext = viewModel
        };

        window.Closed += this.OnSessionWindowClosed;
        window.Show();
    }

    private void OpenViewerWindow(Connection connection)
    {
        RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var viewModel = new ViewerViewModel(connection, this._viewerLogger);
        var window = new ViewerView
        {
            DataContext = viewModel
        };

        window.Closed += this.OnSessionWindowClosed;
        window.Show();
    }

    private async void OnSessionWindowClosed(object? sender, EventArgs e)
    {
        // Reconnect to get fresh credentials
        await this._hubClient.GenerateNewPassword();

        // Show MainView again
        RequestShowMainView?.Invoke(this, EventArgs.Empty);
    }

    public async Task InitializeAsync()
    {
        await this._hubClient.ConnectToHub();
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        await this._hubClient.GenerateNewPassword();

        if (string.IsNullOrWhiteSpace(this.TargetUsername) || string.IsNullOrWhiteSpace(this.TargetPassword))
        {
            this.ConnectionError = "Username and password are required";
            return;
        }

        this.IsConnecting = true;
        this.ConnectionError = null;

        try
        {
            var error = await this._hubClient.ConnectTo(this.TargetUsername, this.TargetPassword);

            if (error is null)
            {
                this.ConnectionError = null;
                // Connection successful - will receive ConnectionStarted event
            }
            else
            {
                this.ConnectionError = error switch
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
            this.IsConnecting = false;
        }
    }
}

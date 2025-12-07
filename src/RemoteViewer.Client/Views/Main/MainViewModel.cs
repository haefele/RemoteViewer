using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Views.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly IViewModelFactory _viewModelFactory;

    [ObservableProperty]
    private string? _yourUsername;

    [ObservableProperty]
    private string? _yourPassword;

    [ObservableProperty]
    private string? _targetUsername;

    [ObservableProperty]
    private string? _targetPassword;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = "Connecting...";

    [ObservableProperty]
    private string? _connectionError;

    [ObservableProperty]
    private bool _isConnecting;

    public event EventHandler? RequestHideMainView;
    public event EventHandler? RequestShowMainView;

    public MainViewModel(ConnectionHubClient hubClient, IViewModelFactory viewModelFactory)
    {
        this._hubClient = hubClient;
        this._viewModelFactory = viewModelFactory;

        this._hubClient.HubConnectionStatusChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.IsConnected = this._hubClient.IsConnected;
                this.StatusText = this._hubClient switch
                {
                    { IsConnected: false, HasVersionMismatch: true } => $"Version mismatch - Server: {this._hubClient.ServerVersion}, Client: {ThisAssembly.AssemblyInformationalVersion}",
                    { IsConnected: false, HasVersionMismatch: false } => "Connecting...",
                    { IsConnected: true } => "Connected",
                };

                this.YourUsername = this._hubClient.IsConnected ? this._hubClient.Username : "...";
                this.YourPassword = this._hubClient.IsConnected ? this._hubClient.Password : "...";
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
            if (e.Connection.IsPresenter)
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
        this.RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var window = new PresenterView
        {
            DataContext = this._viewModelFactory.CreatePresenterViewModel(connection)
        };
        window.Closed += this.OnSessionWindowClosed;
        window.Show();
    }

    private void OpenViewerWindow(Connection connection)
    {
        this.RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var window = new ViewerView
        {
            DataContext = this._viewModelFactory.CreateViewerViewModel(connection)
        };
        window.Closed += this.OnSessionWindowClosed;
        window.Show();
    }

    private void OnSessionWindowClosed(object? sender, EventArgs e)
    {
        this.RequestShowMainView?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
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
            if (error is not null)
            {
                this.ConnectionError = error switch
                {
                    TryConnectError.IncorrectUsernameOrPassword => "Incorrect username or password",
                    TryConnectError.ViewerNotFound => "Connection error - please try again",
                    TryConnectError.CannotConnectToYourself => "Cannot connect to yourself",
                    _ => "Unknown error occurred"
                };
            }
            else
            {
                // Connection successful - will receive ConnectionStarted event
            }
        }
        finally
        {
            this.IsConnecting = false;
        }
    }
}

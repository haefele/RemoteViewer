using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.Presenter;
using RemoteViewer.Client.Views.Viewer;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Views.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly ILogger<MainViewModel> _logger;

    public ToastsViewModel Toasts { get; }

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
    private bool _hasVersionMismatch;

    [ObservableProperty]
    private string _versionMismatchText = string.Empty;

    public event EventHandler? RequestHideMainView;
    public event EventHandler? RequestShowMainView;
    public event EventHandler<string>? CopyToClipboardRequested;

    public MainViewModel(ConnectionHubClient hubClient, IViewModelFactory viewModelFactory, ILogger<MainViewModel> logger)
    {
        this._hubClient = hubClient;
        this._viewModelFactory = viewModelFactory;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        this._hubClient.HubConnectionStatusChanged += (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this.IsConnected = this._hubClient.IsConnected;
                this.HasVersionMismatch = this._hubClient.HasVersionMismatch;

                if (this._hubClient.HasVersionMismatch)
                {
                    this.VersionMismatchText = $"Version mismatch: Server v{this._hubClient.ServerVersion}, Client v{ThisAssembly.AssemblyInformationalVersion}";
                }

                this.StatusText = this._hubClient.IsConnected ? "Connected" : "Connecting...";

                this._logger.HubConnectionStatusChanged(this._hubClient.IsConnected, this.StatusText);

                this.YourUsername = this._hubClient.IsConnected ? this._hubClient.Username : "...";
                this.YourPassword = this._hubClient.IsConnected ? this._hubClient.Password : "...";
            });
        };

        this._hubClient.CredentialsAssigned += (_, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                this._logger.CredentialsAssigned(e.Username);
                this.YourUsername = e.Username;
                this.YourPassword = e.Password;
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
                this._logger.ConnectionSuccessful("Presenter");
                this.OpenPresenterWindow(e.Connection);
            }
            else
            {
                this._logger.ConnectionSuccessful("Viewer");
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
        this._logger.SessionWindowClosed();
        this.RequestShowMainView?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(this.TargetUsername) || string.IsNullOrWhiteSpace(this.TargetPassword))
        {
            this.Toasts.Error("ID and password are required");
            return;
        }

        this._logger.ConnectingToDevice(this.TargetUsername);

        var error = await this._hubClient.ConnectTo(this.TargetUsername, this.TargetPassword);
        if (error is not null)
        {
            var errorMessage = error switch
            {
                TryConnectError.IncorrectUsernameOrPassword => "Incorrect ID or password",
                TryConnectError.ViewerNotFound => "Connection error - please try again",
                TryConnectError.CannotConnectToYourself => "Cannot connect to yourself",
                _ => "Unknown error occurred"
            };

            this._logger.ConnectionFailed(errorMessage);
            this.Toasts.Error(errorMessage);
        }
    }

    [RelayCommand]
    private void CopyCredentials()
    {
        if (this.YourUsername is null || this.YourPassword is null)
            return;

        var text = $"""
                    ID: {this.YourUsername}
                    Password: {this.YourPassword}
                    """;
        this.CopyToClipboardRequested?.Invoke(this, text);
        this.Toasts.Success("ID and password copied to clipboard");

        this._logger.CopiedCredentialsToClipboard();
    }

    [RelayCommand]
    private async Task GenerateNewPasswordAsync()
    {
        await this._hubClient.GenerateNewPassword();
        this._logger.GeneratedNewPassword();
    }
}

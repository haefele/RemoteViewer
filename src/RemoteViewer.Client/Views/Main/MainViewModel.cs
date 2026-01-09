using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Controls.Toasts;
using RemoteViewer.Client.Services.Dialogs;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Views.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ConnectionHubClient _hubClient;
    private readonly IDispatcher _dispatcher;
    private readonly IViewModelFactory _viewModelFactory;
    private readonly IDialogService _dialogService;
    private readonly ILogger<MainViewModel> _logger;

    private IWindowHandle? _sessionWindowHandle;

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

    public MainViewModel(ConnectionHubClient hubClient, IDispatcher dispatcher, IViewModelFactory viewModelFactory, IDialogService dialogService, ILogger<MainViewModel> logger)
    {
        this._hubClient = hubClient;
        this._dispatcher = dispatcher;
        this._viewModelFactory = viewModelFactory;
        this._dialogService = dialogService;
        this._logger = logger;
        this.Toasts = viewModelFactory.CreateToastsViewModel();

        this._hubClient.HubConnectionStatusChanged += (_, _) =>
        {
            this._dispatcher.Post(() =>
            {
                this.IsConnected = this._hubClient.IsConnected;
                this.HasVersionMismatch = this._hubClient.HasVersionMismatch;

                if (this._hubClient.HasVersionMismatch)
                {
                    this.VersionMismatchText = $"""
                                                Version mismatch!
                                                Server v{this._hubClient.ServerVersion}
                                                Client v{ThisAssembly.AssemblyInformationalVersion}
                                                """;
                }

                this.StatusText = this._hubClient.IsConnected ? "Connected" : "Connecting...";

                this._logger.HubConnectionStatusChanged(this._hubClient.IsConnected, this.StatusText);

                this.YourUsername = this._hubClient.IsConnected ? this._hubClient.Username : "...";
                this.YourPassword = this._hubClient.IsConnected ? this._hubClient.Password : "...";
            });
        };

        this._hubClient.CredentialsAssigned += (_, e) =>
        {
            this._dispatcher.Post(() =>
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
        this._dispatcher.Post(() =>
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

        var viewModel = this._viewModelFactory.CreatePresenterViewModel(connection);
        this._sessionWindowHandle = this._dialogService.ShowPresenterWindow(viewModel);
        this._sessionWindowHandle.Closed += this.OnSessionWindowClosed;
    }

    private void OpenViewerWindow(Connection connection)
    {
        this.RequestHideMainView?.Invoke(this, EventArgs.Empty);

        var viewModel = this._viewModelFactory.CreateViewerViewModel(connection);
        this._sessionWindowHandle = this._dialogService.ShowViewerWindow(viewModel);
        this._sessionWindowHandle.Closed += this.OnSessionWindowClosed;
    }

    private void OnSessionWindowClosed(object? sender, EventArgs e)
    {
        this._logger.SessionWindowClosed();

        if (this._sessionWindowHandle is not null)
        {
            this._sessionWindowHandle.Closed -= this.OnSessionWindowClosed;
            this._sessionWindowHandle = null;
        }

        this.RequestShowMainView?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ConnectToDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(this.TargetUsername) || string.IsNullOrWhiteSpace(this.TargetPassword))
        {
            this.Toasts.Error("ID and password are required.");
            return;
        }

        this._logger.ConnectingToDevice(this.TargetUsername);

        var error = await this._hubClient.ConnectTo(this.TargetUsername, this.TargetPassword);
        if (error is not null)
        {
            var errorMessage = error switch
            {
                TryConnectError.IncorrectUsernameOrPassword => "Incorrect ID or password.",
                TryConnectError.ViewerNotFound => "Connection error - please try again.",
                TryConnectError.CannotConnectToYourself => "Cannot connect to yourself.",
                _ => "Unknown error occurred."
            };

            this._logger.ConnectionFailed(errorMessage);
            this.Toasts.Error(errorMessage);
        }
    }

    [RelayCommand]
    private async Task CopyCredentials()
    {
        if (this.YourUsername is null || this.YourPassword is null)
            return;

        var text = $"""
                    ID: {this.YourUsername}
                    Password: {this.YourPassword}
                    """;
        var clipboard = App.Current.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
        this.Toasts.Success("ID and password copied to clipboard.");

        this._logger.CopiedCredentialsToClipboard();
    }

    [RelayCommand]
    private async Task GenerateNewPasswordAsync()
    {
        await this._hubClient.GenerateNewPassword();
        this._logger.GeneratedNewPassword();
    }

    [RelayCommand]
    private async Task ShowAboutAsync()
    {
        await this._dialogService.ShowAboutDialogAsync();
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using RemoteViewer.Client.Services;
using RemoteViewer.Client.Services.ViewModels;
using RemoteViewer.Client.Views.About;

namespace RemoteViewer.Client.Views.Main;

public partial class MainView : Window
{
    private readonly IViewModelFactory _viewModelFactory;
    private MainViewModel? _viewModel;
    private AboutView? _aboutView;

    public MainView() : this(null!)
    {
    }

    public MainView(IViewModelFactory viewModelFactory)
    {
        this._viewModelFactory = viewModelFactory;
        this.InitializeComponent();
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.RequestShowMainView -= this.ViewModel_RequestShowMainView;
            this._viewModel.RequestHideMainView -= this.ViewModel_RequestHideMainView;
            this._viewModel.RequestShowAbout -= this.ViewModel_RequestShowAbout;
        }

        this._viewModel = this.DataContext as MainViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.RequestShowMainView += this.ViewModel_RequestShowMainView;
            this._viewModel.RequestHideMainView += this.ViewModel_RequestHideMainView;
            this._viewModel.RequestShowAbout += this.ViewModel_RequestShowAbout;
        }
    }

    private void ViewModel_RequestHideMainView(object? sender, EventArgs e)
    {
        this.Hide();
    }

    private void ViewModel_RequestShowMainView(object? sender, EventArgs e)
    {
        this.Show();
    }

    private void ViewModel_RequestShowAbout(object? sender, EventArgs e)
    {
        if (this._aboutView is not null)
        {
            this._aboutView.Activate();
            return;
        }

        this._aboutView = new AboutView
        {
            DataContext = this._viewModelFactory.CreateAboutViewModel()
        };
        this._aboutView.Closed += this.AboutView_Closed;
        this._aboutView.Show();
    }

    private void AboutView_Closed(object? sender, EventArgs e)
    {
        if (this._aboutView is not null)
        {
            this._aboutView.Closed -= this.AboutView_Closed;
            this._aboutView = null;
        }
    }

    private void TargetPasswordBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && this._viewModel?.ConnectToDeviceCommand.CanExecute(null) == true)
        {
            this._viewModel.ConnectToDeviceCommand.Execute(null);
        }
    }

    private async void TargetUsernameBox_Pasting(object? sender, RoutedEventArgs e)
    {
        var clipboard = this.Clipboard;
        if (clipboard is null || this._viewModel is null)
            return;

        var text = await clipboard.TryGetTextAsync();
        var (id, password) = CredentialParser.TryParse(text);

        if (id is not null && password is not null)
        {
            e.Handled = true;
            this._viewModel.TargetUsername = id;
            this._viewModel.TargetPassword = password;

            await this._viewModel.ConnectToDeviceCommand.ExecuteAsync(null);
        }
    }
}

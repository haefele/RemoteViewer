using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.Views.Main;

public partial class MainView : Window
{
    private MainViewModel? _viewModel;

    public MainView()
    {
        this.InitializeComponent();
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.RequestShowMainView -= this.ViewModel_RequestShowMainView;
            this._viewModel.RequestHideMainView -= this.ViewModel_RequestHideMainView;
        }

        this._viewModel = this.DataContext as MainViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.RequestShowMainView += this.ViewModel_RequestShowMainView;
            this._viewModel.RequestHideMainView += this.ViewModel_RequestHideMainView;
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

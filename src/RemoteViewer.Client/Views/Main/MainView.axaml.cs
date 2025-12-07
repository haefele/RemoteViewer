using Avalonia.Controls;

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
}

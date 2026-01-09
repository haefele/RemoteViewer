using Avalonia.Controls;

namespace RemoteViewer.Client.Views.About;

public partial class AboutView : Window
{
    private AboutViewModel? _viewModel;

    public AboutView()
    {
        this.InitializeComponent();
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
        }

        this._viewModel = this.DataContext as AboutViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
        }
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }
}

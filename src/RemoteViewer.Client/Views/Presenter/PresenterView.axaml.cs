using Avalonia.Controls;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterView : Window
{
    private PresenterViewModel? _viewModel;

    public PresenterView()
    {
        this.InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested -= this.OnCloseRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.OnCloseRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
    }
}

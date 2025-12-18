using Avalonia.Controls;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterView : Window
{
    private PresenterViewModel? _viewModel;

    public PresenterView()
    {
        this.InitializeComponent();
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested -= this.ViewModel_CopyToClipboardRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested += this.ViewModel_CopyToClipboardRequested;
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    private async void ViewModel_CopyToClipboardRequested(object? sender, string text)
    {
        var clipboard = this.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}

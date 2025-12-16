using Avalonia.Controls;
using Avalonia.Input.Platform;

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
            this._viewModel.CloseRequested -= this.OnCloseRequested;
            this._viewModel.CopyToClipboardRequested -= this.OnCopyToClipboardRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.OnCloseRequested;
            this._viewModel.CopyToClipboardRequested += this.OnCopyToClipboardRequested;
        }
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

    private async void OnCopyToClipboardRequested(object? sender, string text)
    {
        var clipboard = this.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
            await this._viewModel.DisposeAsync();
    }
}

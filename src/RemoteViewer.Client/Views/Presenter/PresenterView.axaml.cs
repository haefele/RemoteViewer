using Avalonia.Controls;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using RemoteViewer.Client.Services.FileTransfer;

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
            this._viewModel.FileTransferConfirmationRequested -= this.ViewModel_FileTransferConfirmationRequested;
            this._viewModel.FileDownloadConfirmationRequested -= this.ViewModel_FileDownloadConfirmationRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested += this.ViewModel_CopyToClipboardRequested;
            this._viewModel.FileTransferConfirmationRequested += this.ViewModel_FileTransferConfirmationRequested;
            this._viewModel.FileDownloadConfirmationRequested += this.ViewModel_FileDownloadConfirmationRequested;
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

    private async void ViewModel_FileTransferConfirmationRequested(object? sender, IncomingFileRequestedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var fileSizeFormatted = FileTransferHelpers.FormatFileSize(e.FileSize);
        var message = $"A viewer wants to send you a file:\n\n{e.FileName} ({fileSizeFormatted})\n\nAccept this file?";

        var box = MessageBoxManager.GetMessageBoxStandard(
            "Incoming File Transfer",
            message,
            ButtonEnum.YesNo,
            MsBox.Avalonia.Enums.Icon.Question);

        var result = await box.ShowWindowDialogAsync(this);

        if (result == ButtonResult.Yes)
        {
            await this._viewModel.AcceptFileTransferAsync(e.SenderClientId, e.TransferId, e.FileName, e.FileSize);
        }
        else
        {
            await this._viewModel.RejectFileTransferAsync(e.SenderClientId, e.TransferId);
        }
    }

    private async void ViewModel_FileDownloadConfirmationRequested(object? sender, DownloadRequestedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var message = $"A viewer wants to download a file from you:\n\n{e.FilePath}\n\nAllow this download?";

        var box = MessageBoxManager.GetMessageBoxStandard(
            "File Download Request",
            message,
            ButtonEnum.YesNo,
            MsBox.Avalonia.Enums.Icon.Question);

        var result = await box.ShowWindowDialogAsync(this);

        if (result == ButtonResult.Yes)
        {
            await this._viewModel.AcceptFileDownloadAsync(e.RequesterClientId, e.TransferId, e.FilePath);
        }
        else
        {
            await this._viewModel.RejectFileDownloadAsync(e.RequesterClientId, e.TransferId);
        }
    }
}

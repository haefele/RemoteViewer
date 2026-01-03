using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Views.Chat;
using RemoteViewer.Client.Controls.Dialogs;
using RemoteViewer.Client.Services.FileTransfer;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterView : Window
{
    private PresenterViewModel? _viewModel;
    private ChatView? _chatView;

    public PresenterView()
    {
        this.InitializeComponent();

        this.AddHandler(DragDrop.DragEnterEvent, this.Window_DragEnter);
        this.AddHandler(DragDrop.DragLeaveEvent, this.Window_DragLeave);
        this.AddHandler(DragDrop.DropEvent, this.Window_Drop);
    }

    private void Window_DataContextChanged(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested -= this.ViewModel_CopyToClipboardRequested;
            this._viewModel.Chat.OpenChatRequested -= this.Chat_OpenChatRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested += this.ViewModel_CopyToClipboardRequested;
            this._viewModel.Chat.OpenChatRequested += this.Chat_OpenChatRequested;
        }
    }

    private void Chat_OpenChatRequested(object? sender, EventArgs e)
    {
        this.ShowChatWindow();
    }

    private void ChatButton_Click(object? sender, RoutedEventArgs e)
    {
        this.ShowChatWindow();
    }

    private void ShowChatWindow()
    {
        if (this._viewModel is null)
            return;

        if (this._chatView is null)
        {
            this._chatView = new ChatView { DataContext = this._viewModel.Chat };
        }

        this._chatView.ShowAndActivate();
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        this._chatView?.ForceClose();

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

    #region File Transfer

    private async void SendFileButton_Click(object? sender, RoutedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        // Step 1: Pick file first
        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select file to send"
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } filePath)
            return;

        // Step 2: Get viewer IDs (show dialog if multiple)
        var viewerIds = await this.GetViewerIdsForTransferAsync(filePath);
        if (viewerIds is null or { Count: 0 })
            return;

        // Step 3: Send file
        await this._viewModel.SendFileToViewersAsync(filePath, viewerIds);
    }

    private async Task<IReadOnlyList<string>?> GetViewerIdsForTransferAsync(string filePath)
    {
        if (this._viewModel is null)
            return null;

        if (this._viewModel.Viewers.Count == 1)
            return [this._viewModel.Viewers[0].ClientId];

        var fileInfo = new FileInfo(filePath);
        var dialog = ViewerSelectionDialog.Create(
            this._viewModel.Viewers,
            fileInfo.Name,
            FileTransferHelpers.FormatFileSize(fileInfo.Length));

        return await dialog.ShowDialog<IReadOnlyList<string>?>(this);
    }

    #endregion

    #region Drag and Drop
    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.IsSingleFileDrag && this._viewModel?.Viewers.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            this.DropOverlay.IsVisible = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Window_DragLeave(object? sender, DragEventArgs e)
    {
        this.DropOverlay.IsVisible = false;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        this.DropOverlay.IsVisible = false;

        if (this._viewModel is null)
            return;

        if (e.SingleFile?.TryGetLocalPath() is not { } filePath)
            return;

        var viewerIds = await this.GetViewerIdsForTransferAsync(filePath);
        if (viewerIds is null or { Count: 0 })
            return;

        await this._viewModel.SendFileToViewersAsync(filePath, viewerIds);
    }
    #endregion
}

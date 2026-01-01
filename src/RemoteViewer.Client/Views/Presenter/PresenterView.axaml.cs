using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using RemoteViewer.Client.Common;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterView : Window
{
    private PresenterViewModel? _viewModel;

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
            this._viewModel.OpenFilePickerRequested -= this.ViewModel_OpenFilePickerRequested;
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
            this._viewModel.CopyToClipboardRequested += this.ViewModel_CopyToClipboardRequested;
            this._viewModel.OpenFilePickerRequested += this.ViewModel_OpenFilePickerRequested;
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

    #region File Transfer

    private void SendFileButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        // Single viewer: directly send (skip flyout)
        if (this._viewModel.Viewers.Count == 1)
        {
            var viewerId = this._viewModel.Viewers[0].ClientId;
            this._viewModel.SendFileCommand.Execute([viewerId]);
            return;
        }

        // Multiple viewers: show the flyout
        if (sender is Button button && this.TryFindResource("ViewerSelectionFlyout", out var resource) && resource is Flyout flyout)
        {
            flyout.ShowAt(button);
        }
    }

    private async void ViewModel_OpenFilePickerRequested(object? sender, IReadOnlyList<string> viewerIds)
    {
        if (this._viewModel is null)
            return;

        var files = await this.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = "Select file to send"
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            await this._viewModel.SendFileFromPathAsync(path);
        }
    }

    private void SendFileToSelected_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        var selectedViewerIds = this._viewModel.Viewers
            .Where(v => v.IsSelected)
            .Select(v => v.ClientId)
            .ToList();

        if (selectedViewerIds.Count == 0)
        {
            this._viewModel.Toasts.Info("No viewers selected.");
            return;
        }

        this._viewModel.SendFileCommand.Execute(selectedViewerIds);
    }

    #endregion

    #region Drag and Drop
    private void Window_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.IsSingleFileDrag && this._viewModel?.Viewers.Count == 1)
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

        if (this._viewModel?.Viewers is not [var viewer])
            return;

        if (e.SingleFile?.TryGetLocalPath() is { } filePath)
        {
            await this._viewModel.SendFileToViewersAsync(filePath, [viewer.ClientId]);
        }
    }
    #endregion
}

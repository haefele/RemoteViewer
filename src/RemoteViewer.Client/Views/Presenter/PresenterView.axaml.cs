using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace RemoteViewer.Client.Views.Presenter;

public partial class PresenterView : Window
{
    private PresenterViewModel? _viewModel;

    public PresenterView()
    {
        this.InitializeComponent();

        // Enable drag-drop for file transfer
        DragDrop.SetAllowDrop(this, true);
        this.AddHandler(DragDrop.DropEvent, this.Window_Drop);
        this.AddHandler(DragDrop.DragOverEvent, this.Window_DragOver);
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

    private void SelectAllViewers_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this._viewModel is null)
            return;

        foreach (var viewer in this._viewModel.Viewers)
        {
            viewer.IsSelected = true;
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

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        if (this._viewModel is null || this._viewModel.Viewers.Count == 0)
            return;

        var files = e.Data.GetFiles();
        if (files is null)
            return;

        // Get selected viewers or all viewers
        var viewerIds = this._viewModel.Viewers
            .Where(v => v.IsSelected)
            .Select(v => v.ClientId)
            .ToList();

        if (viewerIds.Count == 0)
        {
            viewerIds = this._viewModel.Viewers.Select(v => v.ClientId).ToList();
        }

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is { } path)
            {
                await this._viewModel.SendFileToViewersAsync(path, viewerIds);
            }
        }
    }

    #endregion
}

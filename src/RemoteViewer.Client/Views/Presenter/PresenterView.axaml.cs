using Avalonia.Controls;
using Avalonia.Input;
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
        }

        this._viewModel = this.DataContext as PresenterViewModel;

        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested += this.ViewModel_CloseRequested;
        }
    }

    private async void Window_Closed(object? sender, EventArgs e)
    {
        if (this._viewModel is not null)
        {
            this._viewModel.CloseRequested -= this.ViewModel_CloseRequested;
            await this._viewModel.DisposeAsync();
        }
    }

    private void ViewModel_CloseRequested(object? sender, EventArgs e)
    {
        this.Close();
    }

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

        if (this._viewModel.SendFileCommand.CanExecute(filePath))
            await this._viewModel.SendFileCommand.ExecuteAsync(filePath);
    }
    #endregion
}

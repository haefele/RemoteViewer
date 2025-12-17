using Avalonia.Controls;
using RemoteViewer.Client.Views.Viewer;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Controls.FileBrowser;

public partial class FileBrowserWindow : Window
{
    private readonly ViewerViewModel _viewerViewModel;
    private readonly RemoteFileBrowserViewModel _browserViewModel;

    public FileBrowserWindow(ViewerViewModel viewerViewModel)
    {
        this._viewerViewModel = viewerViewModel;
        this._browserViewModel = new RemoteFileBrowserViewModel(viewerViewModel.Connection);

        this.InitializeComponent();

        this.FileBrowser.DataContext = this._browserViewModel;
        this._browserViewModel.FileDownloadRequested += this.OnFileDownloadRequested;

        // Load root directories when window opens
        this.Opened += async (_, _) => await this._browserViewModel.LoadAsync();
        this.Closed += this.OnClosed;
    }

    private async void OnFileDownloadRequested(object? sender, DirectoryEntry entry)
    {
        await this._viewerViewModel.DownloadFileAsync(entry.FullPath);
        this.Close();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        this._browserViewModel.FileDownloadRequested -= this.OnFileDownloadRequested;
        this._browserViewModel.Cleanup();
    }
}

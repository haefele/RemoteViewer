using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Controls.FileBrowser;

public partial class RemoteFileBrowserViewModel : ObservableObject
{
    private readonly Connection _connection;

    [ObservableProperty]
    private string _currentPath = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private DirectoryEntry? _selectedEntry;

    public ObservableCollection<DirectoryEntry> Entries { get; } = [];
    public ObservableCollection<PathSegment> PathSegments { get; } = [];

    public event EventHandler<DirectoryEntry>? FileDownloadRequested;

    public RemoteFileBrowserViewModel(Connection connection)
    {
        this._connection = connection;
    }

    public async Task LoadAsync(string path = "")
    {
        this.IsLoading = true;
        this.ErrorMessage = null;

        try
        {
            var result = await this._connection.FileTransfers.BrowseDirectoryAsync(path);

            if (!result.IsSuccess)
            {
                this.ErrorMessage = result.ErrorMessage;
            }
            else
            {
                this.CurrentPath = result.Path;
                this.UpdatePathSegments();

                this.Entries.Clear();
                foreach (var entry in result.Entries)
                {
                    this.Entries.Add(entry);
                }
            }
        }
        catch (Exception ex)
        {
            this.ErrorMessage = ex.Message;
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    private void UpdatePathSegments()
    {
        this.PathSegments.Clear();

        if (string.IsNullOrEmpty(this.CurrentPath))
            return;

        var parts = this.CurrentPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var part in parts)
        {
            current = string.IsNullOrEmpty(current) ? part + Path.DirectorySeparatorChar : Path.Combine(current, part);
            this.PathSegments.Add(new PathSegment(part, current));
        }
    }

    [RelayCommand]
    private async Task NavigateToEntry(DirectoryEntry entry)
    {
        if (entry.IsDirectory)
        {
            await this.LoadAsync(entry.FullPath);
        }
        else
        {
            this.SelectedEntry = entry;
        }
    }

    [RelayCommand]
    private async Task NavigateUp()
    {
        if (string.IsNullOrEmpty(this.CurrentPath))
            return;

        var parent = Path.GetDirectoryName(this.CurrentPath);
        await this.LoadAsync(parent ?? "");
    }

    [RelayCommand]
    private async Task NavigateToPath(string path)
    {
        await this.LoadAsync(path);
    }

    [RelayCommand]
    private async Task NavigateToRoot()
    {
        await this.LoadAsync("");
    }

    [RelayCommand]
    private void DownloadSelectedFile()
    {
        if (this.SelectedEntry is { IsDirectory: false })
        {
            this.FileDownloadRequested?.Invoke(this, this.SelectedEntry);
        }
    }

    [RelayCommand]
    private void DownloadEntry(DirectoryEntry entry)
    {
        if (!entry.IsDirectory)
        {
            this.FileDownloadRequested?.Invoke(this, entry);
        }
    }
}

public sealed record PathSegment(string Name, string FullPath);

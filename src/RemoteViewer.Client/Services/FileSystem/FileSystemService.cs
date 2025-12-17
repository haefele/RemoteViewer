using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.FileSystem;

public interface IFileSystemService
{
    DirectoryEntry[] GetDirectoryEntries(string path);
    string[] GetRootPaths();
    bool IsPathAllowed(string path);
}

public class FileSystemService : IFileSystemService
{
    private readonly HashSet<string> _allowedRoots;

    public FileSystemService()
    {
        // By default, allow all fixed drives
        this._allowedRoots = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName.ToUpperInvariant())
            .ToHashSet();
    }

    public string[] GetRootPaths()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .Select(d => d.RootDirectory.FullName)
            .ToArray();
    }

    public DirectoryEntry[] GetDirectoryEntries(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!this.IsPathAllowed(fullPath))
            throw new UnauthorizedAccessException("Access to this path is not allowed");

        var entries = new List<DirectoryEntry>();

        // Add directories first
        foreach (var dir in Directory.GetDirectories(fullPath))
        {
            try
            {
                var info = new DirectoryInfo(dir);
                entries.Add(new DirectoryEntry(
                    info.Name,
                    info.FullName,
                    IsDirectory: true,
                    Size: 0));
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible directories
            }
            catch (IOException)
            {
                // Skip directories with I/O errors
            }
        }

        // Add files
        foreach (var file in Directory.GetFiles(fullPath))
        {
            try
            {
                var info = new FileInfo(file);
                entries.Add(new DirectoryEntry(
                    info.Name,
                    info.FullName,
                    IsDirectory: false,
                    info.Length));
            }
            catch (UnauthorizedAccessException)
            {
                // Skip inaccessible files
            }
            catch (IOException)
            {
                // Skip files with I/O errors
            }
        }

        // Sort: directories first, then alphabetically by name
        return entries
            .OrderBy(e => !e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool IsPathAllowed(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var normalizedPath = fullPath.ToUpperInvariant();
            return this._allowedRoots.Any(root => normalizedPath.StartsWith(root, StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }
}

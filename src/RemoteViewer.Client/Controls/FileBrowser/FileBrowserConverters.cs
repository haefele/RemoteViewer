using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Material.Icons;

namespace RemoteViewer.Client.Controls.FileBrowser;

public class BoolToFolderFileIconConverter : IValueConverter
{
    public static readonly BoolToFolderFileIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            return isDirectory ? MaterialIconKind.Folder : MaterialIconKind.File;
        }
        return MaterialIconKind.File;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class FileSizeConverter : IValueConverter
{
    public static readonly FileSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return FormatFileSize(bytes);
        }
        return "";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0)
            return "";

        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        var size = (double)bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}

public class BoolToFolderColorConverter : IValueConverter
{
    public static readonly BoolToFolderColorConverter Instance = new();

    private static readonly IBrush FolderBrush = new SolidColorBrush(Color.Parse("#FFD54F"));
    private static readonly IBrush FileBrush = new SolidColorBrush(Color.Parse("#90CAF9"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDirectory)
        {
            return isDirectory ? FolderBrush : FileBrush;
        }
        return FileBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

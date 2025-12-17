using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace RemoteViewer.Client.Converters;

public class BoolToUploadDownloadIconConverter : IValueConverter
{
    public static readonly BoolToUploadDownloadIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUpload)
        {
            return isUpload ? MaterialIconKind.FileUpload : MaterialIconKind.FileDownload;
        }
        return MaterialIconKind.File;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RemoteViewer.Client.Converters;

public class BoolToUploadDownloadColorConverter : IValueConverter
{
    public static readonly BoolToUploadDownloadColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isUpload)
        {
            // Green for upload, blue for download
            return isUpload
                ? new SolidColorBrush(Color.Parse("#4CAF50"))
                : new SolidColorBrush(Color.Parse("#2196F3"));
        }
        return new SolidColorBrush(Color.Parse("#2196F3"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

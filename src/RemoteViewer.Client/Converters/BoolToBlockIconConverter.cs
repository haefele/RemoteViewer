using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace RemoteViewer.Client.Converters;

public class BoolToBlockIconConverter : IValueConverter
{
    public static readonly BoolToBlockIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBlocked)
        {
            // When blocked, show MouseOff; when not blocked (input allowed), show Mouse
            return isBlocked ? MaterialIconKind.MouseOff : MaterialIconKind.Mouse;
        }
        return MaterialIconKind.Mouse;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

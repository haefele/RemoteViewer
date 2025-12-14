using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace RemoteViewer.Client.Converters;

public class BoolToFullscreenIconConverter : IValueConverter
{
    public static readonly BoolToFullscreenIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isFullscreen)
        {
            return isFullscreen ? MaterialIconKind.FullscreenExit : MaterialIconKind.Fullscreen;
        }
        return MaterialIconKind.Fullscreen;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

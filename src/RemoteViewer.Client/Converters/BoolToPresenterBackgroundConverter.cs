using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RemoteViewer.Client.Converters;

public class BoolToPresenterBackgroundConverter : IValueConverter
{
    public static readonly BoolToPresenterBackgroundConverter Instance = new();

    private static readonly IBrush PresenterBackground = new SolidColorBrush(Color.Parse("#1A0078D4"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true)
        {
            return PresenterBackground;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

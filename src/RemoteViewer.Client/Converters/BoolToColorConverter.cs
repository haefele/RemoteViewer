using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RemoteViewer.Client.Converters;

public class BoolToColorConverter : IValueConverter
{
    public static readonly BoolToColorConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? Color.Parse("#4CAF50") : Color.Parse("#F44336");
        }
        return Color.Parse("#F44336");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

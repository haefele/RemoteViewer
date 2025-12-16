using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class BoolToBlockTooltipConverter : IValueConverter
{
    public static readonly BoolToBlockTooltipConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isBlocked)
        {
            return isBlocked ? "Unblock input" : "Block input";
        }
        return "Block input";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

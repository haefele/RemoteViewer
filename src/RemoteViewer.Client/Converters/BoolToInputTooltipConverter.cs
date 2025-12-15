using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class BoolToInputTooltipConverter : IValueConverter
{
    public static readonly BoolToInputTooltipConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isInputEnabled)
        {
            return isInputEnabled ? "Disable input" : "Enable input";
        }
        return "Disable input";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

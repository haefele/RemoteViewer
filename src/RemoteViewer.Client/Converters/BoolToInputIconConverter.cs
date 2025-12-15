using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace RemoteViewer.Client.Converters;

public class BoolToInputIconConverter : IValueConverter
{
    public static readonly BoolToInputIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isInputEnabled)
        {
            return isInputEnabled ? MaterialIconKind.Mouse : MaterialIconKind.MouseOff;
        }
        return MaterialIconKind.Mouse;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

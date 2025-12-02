using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class BoolToConnectTextConverter : IValueConverter
{
    public static readonly BoolToConnectTextConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnecting)
        {
            return isConnecting ? "Connecting..." : "Connect";
        }
        return "Connect";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

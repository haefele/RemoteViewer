using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RemoteViewer.Client.Converters;

public class BoolToAccentBrushConverter : IValueConverter
{
    public static readonly BoolToAccentBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
            return Brushes.Transparent;

        if (Application.Current?.TryGetResource("SystemControlBackgroundAccentBrush", Application.Current.ActualThemeVariant, out var resource) == true
            && resource is IBrush brush)
        {
            return brush;
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

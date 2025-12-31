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

        // Get accent color from theme resources
        if (Application.Current?.TryGetResource("SystemAccentColor", Application.Current.ActualThemeVariant, out var resource) == true
            && resource is Color accentColor)
        {
            return new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        }

        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace RemoteViewer.Client.Converters;

public class EqualityToBrushConverter : IMultiValueConverter
{
    public static readonly EqualityToBrushConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return Brushes.Transparent;

        var left = values[0];
        var right = values[1];

        if (!Equals(left, right))
            return Brushes.Transparent;

        // Get accent color from theme resources
        if (Application.Current?.TryGetResource("SystemAccentColor", Application.Current.ActualThemeVariant, out var resource) == true
            && resource is Color accentColor)
        {
            return new SolidColorBrush(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B));
        }

        return Brushes.Transparent;
    }
}

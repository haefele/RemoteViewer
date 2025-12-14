using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class ToolbarVisibilityConverter : IMultiValueConverter
{
    public static readonly ToolbarVisibilityConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = IsFullscreen, values[1] = IsToolbarVisible
        var isFullscreen = values.Count > 0 && values[0] is true;
        var isToolbarVisible = values.Count > 1 && values[1] is true;

        // In windowed mode (!isFullscreen): always visible
        // In fullscreen mode: controlled by IsToolbarVisible
        return !isFullscreen || isToolbarVisible;
    }
}

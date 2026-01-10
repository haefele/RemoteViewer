using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class ProgressWidthConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
        {
            return 0.0;
        }

        var progress = values[0] switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            _ => 0.0
        };

        var parentWidth = values[1] switch
        {
            double d => d,
            float f => (double)f,
            int i => (double)i,
            _ => 0.0
        };

        progress = Math.Clamp(progress, 0.0, 1.0);
        return parentWidth * progress;
    }
}

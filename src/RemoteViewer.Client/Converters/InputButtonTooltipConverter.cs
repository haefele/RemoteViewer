using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public class InputButtonTooltipConverter : IMultiValueConverter
{
    public static readonly InputButtonTooltipConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBlocked = values.Count > 0 && values[0] is true;
        var isInputEnabled = values.Count > 1 && values[1] is true;

        if (isBlocked)
            return "Input blocked by presenter";

        return isInputEnabled ? "Disable input" : "Enable input";
    }
}

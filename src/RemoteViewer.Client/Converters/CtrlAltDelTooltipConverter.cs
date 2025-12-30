using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace RemoteViewer.Client.Converters;

public sealed class CtrlAltDelTooltipConverter : IMultiValueConverter
{
    public static readonly CtrlAltDelTooltipConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isBlocked = values.Count > 0 && values[0] is true;
        var canSendSecureAttentionSequence = values.Count > 1 && values[1] is true;

        if (isBlocked)
            return "Input blocked by presenter";

        if (!canSendSecureAttentionSequence)
            return "Ctrl+Alt+Del unavailable";

        return "Send Ctrl+Alt+Del";
    }
}

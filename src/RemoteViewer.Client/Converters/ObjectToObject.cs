using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

namespace RemoteViewer.Client.Converters;

public class MapItem
{
    public object? From { get; set; }
    public object? To { get; set; }
}

public class ObjectToObject : MarkupExtension, IValueConverter
{
    public object? DefaultValue { get; set; }

    [Content]
    public List<MapItem> MapItems { get; set; } = [];

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var item in this.MapItems)
        {
            if (Equals(item.From, value))
                return item.To;
        }
        return this.DefaultValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

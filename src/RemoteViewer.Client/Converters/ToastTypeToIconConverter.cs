using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using RemoteViewer.Client.Controls.Toasts;

namespace RemoteViewer.Client.Converters;

public class ToastTypeToIconConverter : IValueConverter
{
    public static readonly ToastTypeToIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ToastType type)
        {
            return type switch
            {
                ToastType.Success => MaterialIconKind.CheckCircle,
                ToastType.Error => MaterialIconKind.AlertCircle,
                ToastType.Info => MaterialIconKind.InformationOutline,
                _ => MaterialIconKind.InformationOutline
            };
        }
        return MaterialIconKind.InformationOutline;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

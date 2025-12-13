using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using RemoteViewer.Client.Controls.Toasts;

namespace RemoteViewer.Client.Converters;

public class ToastTypeToBackgroundConverter : IValueConverter
{
    public static readonly ToastTypeToBackgroundConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ToastType type)
        {
            return type switch
            {
                ToastType.Success => new SolidColorBrush(Color.Parse("#4CAF50")),
                ToastType.Error => new SolidColorBrush(Color.Parse("#F44336")),
                ToastType.Info => new SolidColorBrush(Color.Parse("#2196F3")),
                _ => new SolidColorBrush(Color.Parse("#2196F3"))
            };
        }
        return new SolidColorBrush(Color.Parse("#2196F3"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

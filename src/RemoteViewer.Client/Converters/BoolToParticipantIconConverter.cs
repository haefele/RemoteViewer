using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;

namespace RemoteViewer.Client.Converters;

public class BoolToParticipantIconConverter : IValueConverter
{
    public static readonly BoolToParticipantIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isPresenter)
        {
            return isPresenter ? MaterialIconKind.Crown : MaterialIconKind.Eye;
        }
        return MaterialIconKind.Eye;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

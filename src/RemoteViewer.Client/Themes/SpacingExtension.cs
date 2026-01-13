using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RemoteViewer.Client.Themes;

public enum SpacingSize
{
    None = 0,
    XXS = 2,
    XS = 4,
    SM = 8,
    MD = 12,
    LG = 16,
    XL = 24,
    XXL = 32
}

public class SpacingExtension : MarkupExtension
{
    public SpacingSize All { get; set; } = SpacingSize.None;
    public SpacingSize? X { get; set; }
    public SpacingSize? Y { get; set; }
    public SpacingSize? Top { get; set; }
    public SpacingSize? Bottom { get; set; }
    public SpacingSize? Left { get; set; }
    public SpacingSize? Right { get; set; }

    public SpacingExtension() { }

    public SpacingExtension(SpacingSize all) => this.All = all;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var target = serviceProvider.GetService(typeof(IProvideValueTarget)) as IProvideValueTarget;
        var targetProperty = target?.TargetProperty;

        Type? targetType = null;
        if (targetProperty is PropertyInfo pi)
            targetType = pi.PropertyType;
        else if (targetProperty is AvaloniaProperty ap)
            targetType = ap.PropertyType;

        if (targetType == typeof(double))
            return (double)this.All;

        if (targetType == typeof(GridLength))
            return new GridLength((double)this.All, GridUnitType.Pixel);

        if (targetType == typeof(Thickness))
        {
            var top = (double)(this.Top ?? this.Y ?? this.All);
            var bottom = (double)(this.Bottom ?? this.Y ?? this.All);
            var left = (double)(this.Left ?? this.X ?? this.All);
            var right = (double)(this.Right ?? this.X ?? this.All);

            return new Thickness(left, top, right, bottom);
        }

        throw new InvalidOperationException($"Cannot convert SpacingExtension to target type '{targetType}'");
    }
}

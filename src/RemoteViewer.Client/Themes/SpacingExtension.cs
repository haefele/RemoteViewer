using Avalonia;
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
        var top = (double)(this.Top ?? this.Y ?? this.All);
        var bottom = (double)(this.Bottom ?? this.Y ?? this.All);
        var left = (double)(this.Left ?? this.X ?? this.All);
        var right = (double)(this.Right ?? this.X ?? this.All);

        return new Thickness(left, top, right, bottom);
    }
}

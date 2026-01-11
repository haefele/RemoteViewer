using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace RemoteViewer.Client.Themes;

public class GridSpacingExtension : MarkupExtension
{
    public SpacingSize Size { get; set; } = SpacingSize.None;

    public GridSpacingExtension() { }

    public GridSpacingExtension(SpacingSize size) => this.Size = size;

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new GridLength((double)this.Size, GridUnitType.Pixel);
}

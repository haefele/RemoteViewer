using Avalonia.Markup.Xaml;

namespace RemoteViewer.Client.Themes;

public enum GapSize
{
    None = 0,
    XXS = 2,
    XS = 4,
    Icon = 6,
    SM = 8,
    MD = 12,
    LG = 16,
    XL = 24,
    XXL = 32
}

public class GapExtension : MarkupExtension
{
    public GapSize Size { get; set; } = GapSize.None;

    public GapExtension() { }

    public GapExtension(GapSize size) => this.Size = size;

    public override object ProvideValue(IServiceProvider serviceProvider) => (double)this.Size;
}

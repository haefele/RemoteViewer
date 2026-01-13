using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Material.Icons;

namespace RemoteViewer.Client.Controls;

public enum IconSize
{
    XXS = 12,
    XS = 16,
    SM = 20,
    MD = 24,
    LG = 32,
    XL = 40,
    XXL = 48
}

public partial class Icon : UserControl
{
    public static readonly StyledProperty<MaterialIconKind> KindProperty =
        AvaloniaProperty.Register<Icon, MaterialIconKind>(nameof(Kind), MaterialIconKind.Star);

    public static readonly StyledProperty<IconSize> SizeProperty =
        AvaloniaProperty.Register<Icon, IconSize>(nameof(Size), IconSize.MD);

    public static readonly StyledProperty<bool> ShowAsBadgeProperty =
        AvaloniaProperty.Register<Icon, bool>(nameof(ShowAsBadge), false);

    public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
        AvaloniaProperty.Register<Icon, IBrush?>(nameof(BadgeBackground));

    public MaterialIconKind Kind
    {
        get => this.GetValue(KindProperty);
        set => this.SetValue(KindProperty, value);
    }

    public IconSize Size
    {
        get => this.GetValue(SizeProperty);
        set => this.SetValue(SizeProperty, value);
    }

    public bool ShowAsBadge
    {
        get => this.GetValue(ShowAsBadgeProperty);
        set => this.SetValue(ShowAsBadgeProperty, value);
    }

    public IBrush? BadgeBackground
    {
        get => this.GetValue(BadgeBackgroundProperty);
        set => this.SetValue(BadgeBackgroundProperty, value);
    }

    public Icon()
    {
        this.InitializeComponent();
    }
}

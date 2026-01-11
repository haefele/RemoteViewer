using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Material.Icons;

namespace RemoteViewer.Client.Controls;

public enum IconBadgeSize
{
    Small,
    Medium,
    Large
}

public partial class IconBadge : UserControl
{
    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<IconBadge, MaterialIconKind>(nameof(Icon), MaterialIconKind.Star);

    public static readonly StyledProperty<IconBadgeSize> SizeProperty =
        AvaloniaProperty.Register<IconBadge, IconBadgeSize>(nameof(Size), IconBadgeSize.Medium);

    public static readonly StyledProperty<IBrush?> IconForegroundProperty =
        AvaloniaProperty.Register<IconBadge, IBrush?>(nameof(IconForeground));

    public static readonly StyledProperty<IBrush?> BadgeBackgroundProperty =
        AvaloniaProperty.Register<IconBadge, IBrush?>(nameof(BadgeBackground));

    public MaterialIconKind Icon
    {
        get => this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    public IconBadgeSize Size
    {
        get => this.GetValue(SizeProperty);
        set => this.SetValue(SizeProperty, value);
    }

    public IBrush? IconForeground
    {
        get => this.GetValue(IconForegroundProperty);
        set => this.SetValue(IconForegroundProperty, value);
    }

    public IBrush? BadgeBackground
    {
        get => this.GetValue(BadgeBackgroundProperty);
        set => this.SetValue(BadgeBackgroundProperty, value);
    }

    public IconBadge()
    {
        this.InitializeComponent();
    }
}

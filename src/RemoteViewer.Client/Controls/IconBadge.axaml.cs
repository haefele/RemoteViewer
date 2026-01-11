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
        this.UpdateSize();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SizeProperty)
        {
            this.UpdateSize();
        }
    }

    private void UpdateSize()
    {
        if (this.BadgeBorder == null || this.BadgeIcon == null)
            return;

        var (badgeSize, cornerRadius, iconSize) = this.Size switch
        {
            IconBadgeSize.Small => (32.0, 8.0, 16.0),
            IconBadgeSize.Medium => (40.0, 10.0, 22.0),
            IconBadgeSize.Large => (64.0, 32.0, 32.0),
            _ => (40.0, 10.0, 22.0)
        };

        this.BadgeBorder.Width = badgeSize;
        this.BadgeBorder.Height = badgeSize;
        this.BadgeBorder.CornerRadius = new CornerRadius(cornerRadius);
        this.BadgeIcon.Width = iconSize;
        this.BadgeIcon.Height = iconSize;
    }
}

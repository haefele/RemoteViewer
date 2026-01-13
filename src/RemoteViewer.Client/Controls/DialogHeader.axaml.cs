using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Material.Icons;

namespace RemoteViewer.Client.Controls;

public partial class DialogHeader : UserControl
{
    public static readonly StyledProperty<MaterialIconKind> IconProperty =
        AvaloniaProperty.Register<DialogHeader, MaterialIconKind>(nameof(Icon), MaterialIconKind.Information);

    public static readonly StyledProperty<IconSize> IconSizeProperty =
        AvaloniaProperty.Register<DialogHeader, IconSize>(nameof(IconSize), IconSize.MD);

    public static readonly StyledProperty<IBrush?> IconForegroundProperty =
        AvaloniaProperty.Register<DialogHeader, IBrush?>(nameof(IconForeground));

    public static readonly StyledProperty<IBrush?> IconBackgroundProperty =
        AvaloniaProperty.Register<DialogHeader, IBrush?>(nameof(IconBackground));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<DialogHeader, string?>(nameof(Subtitle));

    public MaterialIconKind Icon
    {
        get => this.GetValue(IconProperty);
        set => this.SetValue(IconProperty, value);
    }

    public IconSize IconSize
    {
        get => this.GetValue(IconSizeProperty);
        set => this.SetValue(IconSizeProperty, value);
    }

    public IBrush? IconForeground
    {
        get => this.GetValue(IconForegroundProperty);
        set => this.SetValue(IconForegroundProperty, value);
    }

    public IBrush? IconBackground
    {
        get => this.GetValue(IconBackgroundProperty);
        set => this.SetValue(IconBackgroundProperty, value);
    }

    public string? Title
    {
        get => this.GetValue(TitleProperty);
        set => this.SetValue(TitleProperty, value);
    }

    public string? Subtitle
    {
        get => this.GetValue(SubtitleProperty);
        set => this.SetValue(SubtitleProperty, value);
    }

    public DialogHeader()
    {
        this.InitializeComponent();
    }
}

using Avalonia;
using Avalonia.Controls;

namespace RemoteViewer.Client.Controls;

public enum CardVariant
{
    Default,
    Elevated,
    Surface,
    AccentStrip
}

public enum CardAccent
{
    None,
    Success,
    Error,
    Info,
    Warning,
    Primary
}

public partial class Card : ContentControl
{
    public static readonly StyledProperty<CardVariant> VariantProperty =
        AvaloniaProperty.Register<Card, CardVariant>(nameof(Variant), CardVariant.Default);

    public static readonly StyledProperty<CardAccent> AccentProperty =
        AvaloniaProperty.Register<Card, CardAccent>(nameof(Accent), CardAccent.None);

    public CardVariant Variant
    {
        get => this.GetValue(VariantProperty);
        set => this.SetValue(VariantProperty, value);
    }

    public CardAccent Accent
    {
        get => this.GetValue(AccentProperty);
        set => this.SetValue(AccentProperty, value);
    }

    public Card()
    {
        this.InitializeComponent();
    }
}

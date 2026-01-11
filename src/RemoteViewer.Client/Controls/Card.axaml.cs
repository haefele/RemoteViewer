using Avalonia;
using Avalonia.Controls;

namespace RemoteViewer.Client.Controls;

public enum CardSize
{
    Default,
    Large,
    XLarge,
    ListItem
}

public partial class Card : ContentControl
{
    public static readonly StyledProperty<CardSize> SizeProperty =
        AvaloniaProperty.Register<Card, CardSize>(nameof(Size), CardSize.Default);

    public CardSize Size
    {
        get => this.GetValue(SizeProperty);
        set => this.SetValue(SizeProperty, value);
    }

    public Card()
    {
        this.InitializeComponent();
    }
}

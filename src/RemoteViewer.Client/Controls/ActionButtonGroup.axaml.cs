using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Material.Icons;

namespace RemoteViewer.Client.Controls;

public partial class ActionButtonGroup : UserControl
{
    public static readonly StyledProperty<string?> PrimaryTextProperty =
        AvaloniaProperty.Register<ActionButtonGroup, string?>(nameof(PrimaryText));

    public static readonly StyledProperty<ICommand?> PrimaryCommandProperty =
        AvaloniaProperty.Register<ActionButtonGroup, ICommand?>(nameof(PrimaryCommand));

    public static readonly StyledProperty<MaterialIconKind> PrimaryIconProperty =
        AvaloniaProperty.Register<ActionButtonGroup, MaterialIconKind>(nameof(PrimaryIcon), MaterialIconKind.Check);

    public static readonly StyledProperty<bool> ShowPrimaryIconProperty =
        AvaloniaProperty.Register<ActionButtonGroup, bool>(nameof(ShowPrimaryIcon), true);

    public static readonly StyledProperty<string?> SecondaryTextProperty =
        AvaloniaProperty.Register<ActionButtonGroup, string?>(nameof(SecondaryText));

    public static readonly StyledProperty<ICommand?> SecondaryCommandProperty =
        AvaloniaProperty.Register<ActionButtonGroup, ICommand?>(nameof(SecondaryCommand));

    public static readonly StyledProperty<MaterialIconKind> SecondaryIconProperty =
        AvaloniaProperty.Register<ActionButtonGroup, MaterialIconKind>(nameof(SecondaryIcon), MaterialIconKind.Close);

    public static readonly StyledProperty<bool> ShowSecondaryIconProperty =
        AvaloniaProperty.Register<ActionButtonGroup, bool>(nameof(ShowSecondaryIcon), true);

    public string? PrimaryText
    {
        get => this.GetValue(PrimaryTextProperty);
        set => this.SetValue(PrimaryTextProperty, value);
    }

    public ICommand? PrimaryCommand
    {
        get => this.GetValue(PrimaryCommandProperty);
        set => this.SetValue(PrimaryCommandProperty, value);
    }

    public MaterialIconKind PrimaryIcon
    {
        get => this.GetValue(PrimaryIconProperty);
        set => this.SetValue(PrimaryIconProperty, value);
    }

    public bool ShowPrimaryIcon
    {
        get => this.GetValue(ShowPrimaryIconProperty);
        set => this.SetValue(ShowPrimaryIconProperty, value);
    }

    public string? SecondaryText
    {
        get => this.GetValue(SecondaryTextProperty);
        set => this.SetValue(SecondaryTextProperty, value);
    }

    public ICommand? SecondaryCommand
    {
        get => this.GetValue(SecondaryCommandProperty);
        set => this.SetValue(SecondaryCommandProperty, value);
    }

    public MaterialIconKind SecondaryIcon
    {
        get => this.GetValue(SecondaryIconProperty);
        set => this.SetValue(SecondaryIconProperty, value);
    }

    public bool ShowSecondaryIcon
    {
        get => this.GetValue(ShowSecondaryIconProperty);
        set => this.SetValue(ShowSecondaryIconProperty, value);
    }

    public ActionButtonGroup()
    {
        this.InitializeComponent();
    }
}

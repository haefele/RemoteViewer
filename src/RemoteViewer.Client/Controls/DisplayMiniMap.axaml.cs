using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Controls;

public partial class DisplayMiniMap : UserControl
{
    public static readonly StyledProperty<ImmutableList<DisplayInfo>?> AvailableDisplaysProperty =
        AvaloniaProperty.Register<DisplayMiniMap, ImmutableList<DisplayInfo>?>(nameof(AvailableDisplays));

    public static readonly StyledProperty<string?> CurrentDisplayIdProperty =
        AvaloniaProperty.Register<DisplayMiniMap, string?>(nameof(CurrentDisplayId));

    public ImmutableList<DisplayInfo>? AvailableDisplays
    {
        get => this.GetValue(AvailableDisplaysProperty);
        set => this.SetValue(AvailableDisplaysProperty, value);
    }

    public string? CurrentDisplayId
    {
        get => this.GetValue(CurrentDisplayIdProperty);
        set => this.SetValue(CurrentDisplayIdProperty, value);
    }

    public event EventHandler<DisplayInfo>? DisplaySelected;

    public DisplayMiniMap()
    {
        this.InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == AvailableDisplaysProperty || change.Property == CurrentDisplayIdProperty)
        {
            this.UpdateMap();
        }
    }

    private void UpdateMap()
    {
        this.MapCanvas.Children.Clear();

        var displays = this.AvailableDisplays;
        if (displays is null || displays.Count == 0)
        {
            this.MapCanvas.Width = 0;
            this.MapCanvas.Height = 0;
            return;
        }

        // Calculate bounding box
        var minX = displays.Min(d => d.Left);
        var minY = displays.Min(d => d.Top);
        var maxX = displays.Max(d => d.Right);
        var maxY = displays.Max(d => d.Bottom);

        var totalWidth = maxX - minX;
        var totalHeight = maxY - minY;

        if (totalWidth <= 0 || totalHeight <= 0)
            return;

        // Scale to fit in max dimensions, with minimum display size
        const double maxCanvasWidth = 280;
        const double maxCanvasHeight = 160;
        const double minDisplayWidth = 65;
        const double minDisplayHeight = 35;
        const double gap = 2;

        var scale = Math.Min(maxCanvasWidth / totalWidth, maxCanvasHeight / totalHeight);

        // Ensure minimum display size
        var smallestDisplayWidth = displays.Min(d => d.Width);
        var smallestDisplayHeight = displays.Min(d => d.Height);
        var minScaleForWidth = minDisplayWidth / smallestDisplayWidth;
        var minScaleForHeight = minDisplayHeight / smallestDisplayHeight;
        scale = Math.Max(scale, Math.Max(minScaleForWidth, minScaleForHeight));

        this.MapCanvas.Width = totalWidth * scale;
        this.MapCanvas.Height = totalHeight * scale;

        // Get accent color from theme
        var accentColor = Color.FromRgb(0, 120, 212); // fallback
        if (Application.Current?.TryGetResource("SystemAccentColor", Application.Current.ActualThemeVariant, out var resource) == true
            && resource is Color themeAccent)
        {
            accentColor = themeAccent;
        }

        var accentBackground = new SolidColorBrush(Color.FromArgb(60, accentColor.R, accentColor.G, accentColor.B));
        var accentBorder = new SolidColorBrush(accentColor);
        var accentForeground = new SolidColorBrush(accentColor);

        foreach (var display in displays)
        {
            var isCurrent = display.Id == this.CurrentDisplayId;

            var textBlock = new TextBlock
            {
                Text = display.FriendlyName,
                FontSize = 11,
                FontWeight = isCurrent ? FontWeight.SemiBold : FontWeight.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (isCurrent)
            {
                textBlock.Foreground = accentForeground;
            }

            var button = new Button
            {
                Width = Math.Max(display.Width * scale - gap, minDisplayWidth),
                Height = Math.Max(display.Height * scale - gap, minDisplayHeight),
                Tag = display,
                Padding = new Thickness(4, 2),
                CornerRadius = new CornerRadius(3),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Content = textBlock
            };

            if (isCurrent)
            {
                button.Background = accentBackground;
                button.BorderBrush = accentBorder;
                button.BorderThickness = new Thickness(2);
            }

            button.Click += this.OnDisplayClicked;

            Canvas.SetLeft(button, (display.Left - minX) * scale + gap / 2);
            Canvas.SetTop(button, (display.Top - minY) * scale + gap / 2);
            this.MapCanvas.Children.Add(button);
        }
    }

    private void OnDisplayClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { Tag: DisplayInfo display })
        {
            this.DisplaySelected?.Invoke(this, display);
        }
    }
}

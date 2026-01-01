using Avalonia;
using Avalonia.Controls;

namespace RemoteViewer.Client.Controls;

public enum DropOverlayMode
{
    Viewer,
    Presenter
}

public partial class DropOverlay : UserControl
{
    public static readonly StyledProperty<DropOverlayMode> ModeProperty =
        AvaloniaProperty.Register<DropOverlay, DropOverlayMode>(nameof(Mode));

    public DropOverlayMode Mode
    {
        get => this.GetValue(ModeProperty);
        set => this.SetValue(ModeProperty, value);
    }

    public DropOverlay()
    {
        this.InitializeComponent();
        this.UpdateSubtitle();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ModeProperty)
        {
            this.UpdateSubtitle();
        }
    }

    private void UpdateSubtitle()
    {
        this.SubtitleText.Text = this.Mode switch
        {
            DropOverlayMode.Viewer => "Release to transfer file to presenter",
            DropOverlayMode.Presenter => "Release to transfer file to viewers",
            _ => "Release to transfer file"
        };
    }
}

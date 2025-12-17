using Avalonia.Controls;
using Avalonia.Input;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Controls.FileBrowser;

public partial class RemoteFileBrowserView : UserControl
{
    public RemoteFileBrowserView()
    {
        this.InitializeComponent();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Find the ListBox and attach double-click handler
        var listBox = this.FindControl<ListBox>("FileListBox");
        if (listBox is not null)
        {
            listBox.DoubleTapped += this.ListBox_DoubleTapped;
        }
    }

    private void ListBox_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (this.DataContext is RemoteFileBrowserViewModel vm && vm.SelectedEntry is { } entry)
        {
            vm.NavigateToEntryCommand.Execute(entry);
        }
    }
}

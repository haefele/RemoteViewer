using Avalonia.Controls;
using Avalonia.Interactivity;

namespace RemoteViewer.Client.Controls.Dialogs;

public partial class FileTransferConfirmationDialog : Window
{
    public FileTransferConfirmationDialog()
    {
        this.InitializeComponent();
    }

    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        this.Close(true);
    }

    private void OnRejectClicked(object? sender, RoutedEventArgs e)
    {
        this.Close(false);
    }
}

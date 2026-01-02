using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;

namespace RemoteViewer.Client.Controls.Dialogs;

public partial class FileTransferConfirmationDialog : Window
{
    public FileTransferConfirmationDialog()
    {
        this.InitializeComponent();
    }

    public static FileTransferConfirmationDialog Create(string displayName, string fileName, string fileSizeFormatted)
    {
        var dialog = new FileTransferConfirmationDialog();
        dialog.HeaderIcon.Kind = MaterialIconKind.FileDownload;
        dialog.HeaderText.Text = "Incoming File Transfer";
        dialog.SenderText.Text = $"\"{displayName}\" wants to send you:";
        dialog.FileNameText.Text = fileName;
        dialog.FileSizeText.Text = fileSizeFormatted;
        return dialog;
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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Material.Icons;

namespace RemoteViewer.Client.Controls.Dialogs;

public partial class FileTransferConfirmationDialog : Window
{
    private readonly TaskCompletionSource<bool> _resultTcs = new();

    public FileTransferConfirmationDialog()
    {
        this.InitializeComponent();
    }

    public Task<bool> ResultTask => this._resultTcs.Task;

    public static FileTransferConfirmationDialog CreateForUpload(string displayName, string fileName, string fileSizeFormatted)
    {
        var dialog = new FileTransferConfirmationDialog();
        dialog.HeaderIcon.Kind = MaterialIconKind.FileUpload;
        dialog.HeaderText.Text = "Incoming File Transfer";
        dialog.SenderText.Text = $"\"{displayName}\" wants to send you:";
        dialog.FileNameText.Text = fileName;
        dialog.FileSizeText.Text = fileSizeFormatted;
        return dialog;
    }

    public static FileTransferConfirmationDialog CreateForDownload(string displayName, string filePath)
    {
        var dialog = new FileTransferConfirmationDialog();
        dialog.HeaderIcon.Kind = MaterialIconKind.FileDownload;
        dialog.HeaderIcon.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2196F3"));
        dialog.HeaderText.Text = "File Download Request";
        dialog.SenderText.Text = $"\"{displayName}\" wants to download:";
        dialog.FileNameText.Text = filePath;
        dialog.FileSizeText.IsVisible = false;
        return dialog;
    }

    private void OnAcceptClicked(object? sender, RoutedEventArgs e)
    {
        this._resultTcs.TrySetResult(true);
        this.Close();
    }

    private void OnRejectClicked(object? sender, RoutedEventArgs e)
    {
        this._resultTcs.TrySetResult(false);
        this.Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // If closed without clicking a button (e.g., X button), treat as reject
        this._resultTcs.TrySetResult(false);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace RemoteViewer.Client.Controls.Dialogs;

public class FileTransferConfirmationDialogViewModel : ObservableObject
{
    public string SenderDisplayName { get; }
    public string FileName { get; }
    public string FileSizeFormatted { get; }

    public FileTransferConfirmationDialogViewModel(
        string senderDisplayName,
        string fileName,
        string fileSizeFormatted)
    {
        this.SenderDisplayName = senderDisplayName;
        this.FileName = fileName;
        this.FileSizeFormatted = fileSizeFormatted;
    }
}

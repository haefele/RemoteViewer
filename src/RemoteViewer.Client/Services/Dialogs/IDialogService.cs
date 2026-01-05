namespace RemoteViewer.Client.Services.Dialogs;

public interface IDialogService
{
    Task<bool> ShowFileTransferConfirmationAsync(string senderDisplayName, string fileName, string fileSizeFormatted);
}

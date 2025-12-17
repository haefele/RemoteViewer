using System.ComponentModel;

namespace RemoteViewer.Client.Services.FileTransfer;

public interface IFileTransfer : INotifyPropertyChanged, IDisposable
{
    string TransferId { get; }
    string? FileName { get; }
    long FileSize { get; }
    double Progress { get; }
    int ProgressPercent { get; }
    FileTransferState State { get; }
    string? ErrorMessage { get; }
    string FileSizeFormatted { get; }

    Task CancelAsync();

    event EventHandler? Completed;
    event EventHandler? Failed;
}

public enum FileTransferState
{
    Pending,
    WaitingForAcceptance,
    Transferring,
    Completed,
    Failed,
    Cancelled,
    Rejected
}

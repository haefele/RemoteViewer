using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RemoteViewer.Client.Services.Dispatching;
using RemoteViewer.Client.Services.FileTransfer;

namespace RemoteViewer.Client.Controls.Toasts;

public partial class TransferToastItemViewModel : ObservableObject, IDisposable
{
    private readonly IFileTransfer _transfer;
    private readonly IDispatcher _dispatcher;
    private readonly Action<TransferToastItemViewModel, bool, string?> _completionCallback;
    private bool _disposed;

    public TransferToastItemViewModel(
        IFileTransfer transfer,
        bool isUpload,
        IDispatcher dispatcher,
        Action<TransferToastItemViewModel, bool, string?> completionCallback)
    {
        this._transfer = transfer;
        this.IsUpload = isUpload;
        this._dispatcher = dispatcher;
        this._completionCallback = completionCallback;

        this._transfer.Completed += this.OnTransferCompleted;
        this._transfer.Failed += this.OnTransferFailed;
    }

    public IFileTransfer Transfer => this._transfer;
    public bool IsUpload { get; }

    private void OnTransferCompleted(object? sender, EventArgs e)
    {
        this._dispatcher.Post(() =>
        {
            this._completionCallback(this, true, null);
        });
    }

    private void OnTransferFailed(object? sender, EventArgs e)
    {
        this._dispatcher.Post(() =>
        {
            this._completionCallback(this, false, this._transfer.ErrorMessage);
        });
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await this._transfer.CancelAsync();
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;

        this._transfer.Completed -= this.OnTransferCompleted;
        this._transfer.Failed -= this.OnTransferFailed;

        GC.SuppressFinalize(this);
    }
}

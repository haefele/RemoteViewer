using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.FileTransfer;

internal interface IFileTransferServiceImpl
{
    Task HandleFileSendRequestAsync(string senderClientId, string transferId, string fileName, long fileSize);
    void HandleFileSendResponse(string transferId, bool accepted, string? errorMessage);
    void HandleFileChunk(string senderClientId, FileChunkMessage chunk);
    void HandleFileComplete(string senderClientId, string transferId);
    void HandleFileCancel(string senderClientId, string transferId, string reason);
    void HandleFileError(string senderClientId, string transferId, string errorMessage);
}

using RemoteViewer.Shared;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IConnectionImpl
{
    void OnMessageReceived(string senderClientId, string messageType, byte[] data);
    void OnConnectionChanged(ConnectionInfo connectionInfo);
    void OnClosed();

    Task SwitchDisplayAsync();
    Task SendInputAsync(string messageType, ReadOnlyMemory<byte> data);
    Task SendFrameAsync(string displayId, ulong frameNumber, FrameCodec codec, FrameRegion[] regions);
    Task SendFileSendRequestAsync(string transferId, string fileName, long fileSize, string? targetClientId = null);
    Task SendFileSendResponseAsync(string transferId, bool accepted, string? error, string? targetClientId = null);
    Task SendFileChunkAsync(FileChunkMessage chunk, string? targetClientId = null);
    Task SendFileCompleteAsync(string transferId, string? targetClientId = null);
    Task SendFileCancelAsync(string transferId, string reason, string? targetClientId = null);
    Task SendFileErrorAsync(string transferId, string error, string? targetClientId = null);
}

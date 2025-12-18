using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

// File send messages (Viewer → Presenter → Viewer)
[GenerateShape]
public sealed partial record FileSendRequestMessage(
    string TransferId,
    string FileName,
    long FileSize
);

[GenerateShape]
public sealed partial record FileSendResponseMessage(
    string TransferId,
    bool Accepted,
    string? ErrorMessage
);

// File transfer data messages (Bidirectional)
[GenerateShape]
public sealed partial record FileChunkMessage(
    string TransferId,
    int ChunkIndex,
    int TotalChunks,
    ReadOnlyMemory<byte> Data
);

[GenerateShape]
public sealed partial record FileCompleteMessage(string TransferId);

[GenerateShape]
public sealed partial record FileCancelMessage(string TransferId, string Reason);

[GenerateShape]
public sealed partial record FileErrorMessage(string TransferId, string ErrorMessage);

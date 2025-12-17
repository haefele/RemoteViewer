using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

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

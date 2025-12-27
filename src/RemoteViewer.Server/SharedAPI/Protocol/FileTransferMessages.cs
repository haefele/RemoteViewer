namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record FileSendRequestMessage(
    string TransferId,
    string FileName,
    long FileSize
);

public sealed record FileSendResponseMessage(
    string TransferId,
    bool Accepted,
    string? ErrorMessage
);

public sealed record FileChunkMessage(
    string TransferId,
    int ChunkIndex,
    int TotalChunks,
    ReadOnlyMemory<byte> Data
);

public sealed record FileCompleteMessage(string TransferId);

public sealed record FileCancelMessage(string TransferId, string Reason);

public sealed record FileErrorMessage(string TransferId, string ErrorMessage);

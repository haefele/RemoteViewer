namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record ChatMessage(
    string SenderClientId,
    string SenderDisplayName,
    string Text,
    long TimestampUtc);

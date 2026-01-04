namespace RemoteViewer.Server.SharedAPI.Protocol;

public sealed record ClipboardTextMessage(string Text);

public sealed record ClipboardImageMessage(ReadOnlyMemory<byte> Data);

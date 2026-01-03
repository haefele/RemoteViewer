using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.HubClient;

internal static partial class ChatServiceLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Chat service started")]
    public static partial void ChatServiceStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chat message sent ({Length} chars)")]
    public static partial void ChatMessageSent(this ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Chat message received from {SenderName} ({Length} chars)")]
    public static partial void ChatMessageReceived(this ILogger logger, string senderName, int length);

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat service stopped")]
    public static partial void ChatServiceStopped(this ILogger logger);
}

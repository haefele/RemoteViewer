using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.HubClient;

internal static partial class ClipboardSyncServiceLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Clipboard sync service started")]
    public static partial void ClipboardSyncStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Clipboard sync service stopped")]
    public static partial void ClipboardSyncStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Error checking clipboard")]
    public static partial void ErrorCheckingClipboard(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to get clipboard image")]
    public static partial void FailedToGetClipboardImage(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent clipboard text ({Length} chars)")]
    public static partial void SentClipboardText(this ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent clipboard image ({Size} bytes)")]
    public static partial void SentClipboardImage(this ILogger logger, int size);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received clipboard text ({Length} chars)")]
    public static partial void ReceivedClipboardText(this ILogger logger, int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to set clipboard text")]
    public static partial void FailedToSetClipboardText(this ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Received clipboard image ({Size} bytes)")]
    public static partial void ReceivedClipboardImage(this ILogger logger, int size);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to set clipboard image")]
    public static partial void FailedToSetClipboardImage(this ILogger logger, Exception exception);
}

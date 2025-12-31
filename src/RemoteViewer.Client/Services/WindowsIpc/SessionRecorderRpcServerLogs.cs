using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.WindowsIpc;

internal static partial class SessionRecorderRpcServerLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Created shared memory for display {DisplayId}: {Name} ({Width}x{Height})")]
    public static partial void SharedMemoryCreated(this ILogger logger, string displayId, string name, int width, int height);

    [LoggerMessage(Level = LogLevel.Information, Message = "Display {DisplayId} resolution changed from {OldWidth}x{OldHeight} to {NewWidth}x{NewHeight}, recreating shared memory")]
    public static partial void SharedMemoryResolutionChanged(this ILogger logger, string displayId, int oldWidth, int oldHeight, int newWidth, int newHeight);

    [LoggerMessage(Level = LogLevel.Error, Message = "SendSAS failed")]
    public static partial void SendSasFailed(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection {ConnectionId} authenticated successfully")]
    public static partial void ConnectionAuthenticated(this ILogger logger, string connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authentication rejected: invalid or expired token")]
    public static partial void AuthenticationRejected(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Authentication failed")]
    public static partial void AuthenticationFailed(this ILogger logger, Exception ex);
}

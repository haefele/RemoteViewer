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
}

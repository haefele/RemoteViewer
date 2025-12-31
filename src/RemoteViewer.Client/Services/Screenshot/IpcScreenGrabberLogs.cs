using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.Screenshot;

internal static partial class IpcScreenGrabberLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "IPC capture failed for display {DisplayId}")]
    public static partial void CaptureError(this ILogger logger, string displayId, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened shared memory for display {DisplayId}: {Name}")]
    public static partial void SharedMemoryOpened(this ILogger logger, string displayId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Display {DisplayId} resolution changed from {OldWidth}x{OldHeight} to {NewWidth}x{NewHeight}, reopening shared memory")]
    public static partial void SharedMemoryResolutionChanged(this ILogger logger, string displayId, int oldWidth, int oldHeight, int newWidth, int newHeight);
}

using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.LocalInputMonitor;

internal static partial class WindowsLocalInputMonitorServiceLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Local input monitoring started")]
    public static partial void MonitoringStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local input monitoring stopped")]
    public static partial void MonitoringStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to install keyboard hook. Win32 error: {ErrorCode}")]
    public static partial void KeyboardHookFailed(this ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to install mouse hook. Win32 error: {ErrorCode}")]
    public static partial void MouseHookFailed(this ILogger logger, int errorCode);
}

using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.ScreenCapture;

internal static partial class DisplayCaptureManagerLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Display capture manager started")]
    public static partial void ManagerStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Display capture manager stopped")]
    public static partial void ManagerStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Monitor loop started")]
    public static partial void MonitorLoopStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Monitor loop stopped")]
    public static partial void MonitorLoopStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting capture pipeline for display {DisplayName}")]
    public static partial void StartingPipeline(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping capture pipeline for display {DisplayName}")]
    public static partial void StoppingPipeline(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Faulted pipeline detected for display {DisplayName}, restarting")]
    public static partial void FaultedPipelineDetected(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display {DisplayName} requested but not found in display list")]
    public static partial void DisplayNotFound(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Monitor loop wait timed out during dispose")]
    public static partial void MonitorLoopTimedOut(this ILogger logger);
}

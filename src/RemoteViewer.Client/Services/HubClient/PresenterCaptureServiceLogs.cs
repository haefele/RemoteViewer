using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.HubClient;

internal static partial class PresenterCaptureServiceLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Presenter capture service started")]
    public static partial void ServiceStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Presenter capture service stopped")]
    public static partial void ServiceStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Monitor loop started")]
    public static partial void MonitorLoopStarted(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Monitor loop stopped")]
    public static partial void MonitorLoopStopped(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting capture pipeline for display {DisplayId}")]
    public static partial void StartingPipeline(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping capture pipeline for display {DisplayId}")]
    public static partial void StoppingPipeline(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Faulted pipeline detected for display {DisplayId}, restarting")]
    public static partial void FaultedPipelineDetected(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display {DisplayId} requested but not found in display list")]
    public static partial void DisplayNotFound(this ILogger logger, string displayId);
}

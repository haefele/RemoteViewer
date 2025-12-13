using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Services.ScreenCapture;

internal static partial class DisplayCapturePipelineLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline started for display {DisplayName}")]
    public static partial void PipelineStarted(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline stopped for display {DisplayName}")]
    public static partial void PipelineStopped(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Capture loop started for display {DisplayName}")]
    public static partial void CaptureLoopStarted(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen grab failed for display {DisplayName}")]
    public static partial void ScreenGrabFailed(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Capture loop failed for display {DisplayName}")]
    public static partial void CaptureLoopFailed(this ILogger logger, Exception exception, string displayName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encode loop started for display {DisplayName}")]
    public static partial void EncodeLoopStarted(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Encode loop failed for display {DisplayName}")]
    public static partial void EncodeLoopFailed(this ILogger logger, Exception exception, string displayName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Send loop started for display {DisplayName}")]
    public static partial void SendLoopStarted(this ILogger logger, string displayName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send loop failed for display {DisplayName}")]
    public static partial void SendLoopFailed(this ILogger logger, Exception exception, string displayName);
}

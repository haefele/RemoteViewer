using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.ScreenCapture;

internal static partial class DisplayCapturePipelineLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline started for display {DisplayId}")]
    public static partial void PipelineStarted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Pipeline stopped for display {DisplayId}")]
    public static partial void PipelineStopped(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Capture loop started for display {DisplayId}")]
    public static partial void CaptureLoopStarted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Capture loop completed for display {DisplayId}")]
    public static partial void CaptureLoopCompleted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screen grab failed for display {DisplayId}, status: {GrabStatus}")]
    public static partial void ScreenGrabFailed(this ILogger logger, string displayId, GrabStatus grabStatus);

    [LoggerMessage(Level = LogLevel.Error, Message = "Capture loop failed for display {DisplayId}")]
    public static partial void CaptureLoopFailed(this ILogger logger, Exception exception, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encode loop started for display {DisplayId}")]
    public static partial void EncodeLoopStarted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encode loop completed for display {DisplayId}")]
    public static partial void EncodeLoopCompleted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Encode loop failed for display {DisplayId}")]
    public static partial void EncodeLoopFailed(this ILogger logger, Exception exception, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Send loop started for display {DisplayId}")]
    public static partial void SendLoopStarted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Send loop completed for display {DisplayId}")]
    public static partial void SendLoopCompleted(this ILogger logger, string displayId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Send loop failed for display {DisplayId}")]
    public static partial void SendLoopFailed(this ILogger logger, Exception exception, string displayId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Captured frame dropped for display {DisplayId}, frame {FrameNumber} (encoder backpressure)")]
    public static partial void CapturedFrameDropped(this ILogger logger, string displayId, ulong frameNumber);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Encoded frame dropped for display {DisplayId}, frame {FrameNumber} (send backpressure)")]
    public static partial void EncodedFrameDropped(this ILogger logger, string displayId, ulong frameNumber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pipeline dispose timed out for display {DisplayId}, tasks may still be running")]
    public static partial void DisposeTimedOut(this ILogger logger, string displayId);
}

namespace RemoteViewer.Client.Services;

public static class BandwidthEstimator
{
    public static string Calculate(int screenWidth, int screenHeight, int targetFps, int quality)
    {
        var rawBytesPerFrame = screenWidth * screenHeight * 3; // RGB bytes

        // Compression ratio: exponential curve matching JPEG behavior for screenshots
        var compressionRatio = 0.005 * Math.Exp(quality * 0.03);

        // 1 keyframe + (fps-1) partial frames at 30% size
        var keyframeBytes = rawBytesPerFrame * compressionRatio;
        var partialFrameBytes = Math.Max(0, targetFps - 1) * rawBytesPerFrame * 0.30 * compressionRatio;
        var totalBytesPerSec = keyframeBytes + partialFrameBytes;

        return totalBytesPerSec switch
        {
            >= 1_000_000 => $"Est. bandwidth: ~{totalBytesPerSec / 1_000_000:F1} MB/s",
            >= 1_000 => $"Est. bandwidth: ~{totalBytesPerSec / 1_000:F0} KB/s",
            _ => $"Est. bandwidth: ~{totalBytesPerSec:F0} B/s"
        };
    }
}

using System.Diagnostics;

namespace RemoteViewer.Client.Common;

public static class SleepHelper
{
    private static readonly TimeSpan s_taskDelayThreshold = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// High-precision delay using spin-wait. Burns CPU but very accurate.
    /// Best for: frame-rate critical code where precision matters.
    /// </summary>
    public static async Task DelayPrecise(TimeSpan delay, CancellationToken ct = default)
    {
        if (delay <= TimeSpan.Zero)
            return;

        var targetTime = Stopwatch.GetTimestamp() + (long)(delay.TotalSeconds * Stopwatch.Frequency);

        // Only use Task.Delay for longer delays where it's actually useful
        if (delay > s_taskDelayThreshold)
        {
            await Task.Delay(delay - s_taskDelayThreshold, ct);
        }

        // Spin-wait for the rest (or the entire duration for short delays)
        while (Stopwatch.GetTimestamp() < targetTime)
        {
            if (ct.IsCancellationRequested)
                return;

            Thread.SpinWait(10);
        }
    }
}

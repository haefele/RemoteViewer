using System.Collections.Concurrent;

namespace RemoteViewer.Client.Services;

public class BandwidthTracker
{
    private readonly record struct Sample(long Timestamp, int Bytes);
    private readonly ConcurrentQueue<Sample> _samples = new();
    private readonly TimeProvider _timeProvider;

    private const int WindowSeconds = 5;

    public BandwidthTracker() : this(TimeProvider.System)
    {
    }

    public BandwidthTracker(TimeProvider timeProvider)
    {
        this._timeProvider = timeProvider;
    }

    public void AddBytes(int count)
    {
        var now = this._timeProvider.GetTimestamp();
        this._samples.Enqueue(new Sample(now, count));
    }

    public double GetBytesPerSecond()
    {
        var now = this._timeProvider.GetTimestamp();
        var cutoff = now - this._timeProvider.TimestampFrequency * WindowSeconds;

        // Prune old samples
        while (this._samples.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            this._samples.TryDequeue(out _);

        // Sum bytes in window, divide by window size to get bytes/sec
        return this._samples.Sum(s => s.Bytes) / (double)WindowSeconds;
    }

    public string GetFormatted()
    {
        var bps = this.GetBytesPerSecond();
        return bps switch
        {
            >= 1_000_000 => $"Bandwidth: {bps / 1_000_000:F1} MB/s",
            >= 1_000 => $"Bandwidth: {bps / 1_000:F0} KB/s",
            _ => $"Bandwidth: {bps:F0} B/s"
        };
    }
}

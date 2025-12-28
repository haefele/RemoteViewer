using System.Buffers;
using System.Collections.Concurrent;

namespace RemoteViewer.Client.Common;

public sealed class TieredBoundedPool : ArrayPool<byte>
{
    private const int LohThreshold = 85_000;
    private const int HugeThreshold = 8 * 1024 * 1024; // 8 MB
    private const int MaxHugeArrays = 3;

    // Size buckets: (size, maxCount)
    private static readonly (int Size, int MaxCount)[] s_buckets =
    [
        (128 * 1024, 16),      // 128 KB - file chunks
        (512 * 1024, 8),       // 512 KB - small dirty regions
        (2 * 1024 * 1024, 8),  // 2 MB - medium regions
        (8 * 1024 * 1024, 4),  // 8 MB - 1080p frames
    ];

    private readonly ConcurrentQueue<byte[]>[] _buckets;
    private readonly int[] _bucketCounts;

    // Huge arrays (> 8 MB) - any size, max 2
    private readonly ConcurrentQueue<byte[]> _hugePool = new();
    private int _hugeCount;

    private readonly PoolMetrics _metrics;

    public TieredBoundedPool()
    {
        this._buckets = new ConcurrentQueue<byte[]>[s_buckets.Length];
        this._bucketCounts = new int[s_buckets.Length];
        for (var i = 0; i < this._buckets.Length; i++)
            this._buckets[i] = new ConcurrentQueue<byte[]>();
        this._metrics = new();
    }

    public override byte[] Rent(int minimumLength)
    {
        this._metrics.RecordRent();

        // Small arrays: always allocate fresh (Gen0 GC is cheap)
        if (minimumLength < LohThreshold)
            return new byte[minimumLength];

        // Huge arrays (> 8 MB)
        if (minimumLength > HugeThreshold)
            return this.RentHuge(minimumLength);

        // Regular LOH arrays - find appropriate bucket
        var bucketIndex = GetBucketIndex(minimumLength);
        if (bucketIndex >= 0 && this._buckets[bucketIndex].TryDequeue(out var array))
        {
            Interlocked.Decrement(ref this._bucketCounts[bucketIndex]);
            this._metrics.RecordHit();
            this._metrics.RemoveRetained(array.Length);
            return array;
        }

        // No pooled array available
        this._metrics.RecordMiss();
        var allocSize = bucketIndex >= 0 ? s_buckets[bucketIndex].Size : minimumLength;
        return new byte[allocSize];
    }

    private byte[] RentHuge(int minimumLength)
    {
        // Try to find a suitable array in the huge pool
        var attempts = Volatile.Read(ref this._hugeCount);
        for (var i = 0; i < attempts; i++)
        {
            if (this._hugePool.TryDequeue(out var array))
            {
                if (array.Length >= minimumLength)
                {
                    Interlocked.Decrement(ref this._hugeCount);
                    this._metrics.RecordHit();
                    this._metrics.RemoveRetained(array.Length);
                    return array;
                }

                // Too small, put it back
                this._hugePool.Enqueue(array);
            }
        }

        this._metrics.RecordMiss();
        return new byte[minimumLength];
    }

    public override void Return(byte[] array, bool clearArray = false)
    {
        if (array.Length < LohThreshold)
            return; // Let GC handle small arrays

        if (clearArray)
            Array.Clear(array);

        // Huge arrays (> 8 MB)
        if (array.Length > HugeThreshold)
        {
            this.ReturnHuge(array);
            return;
        }

        // Regular LOH arrays
        var bucketIndex = GetBucketIndex(array.Length);
        if (bucketIndex < 0)
            return;

        // Check per-bucket limit
        if (Interlocked.Increment(ref this._bucketCounts[bucketIndex]) > s_buckets[bucketIndex].MaxCount)
        {
            Interlocked.Decrement(ref this._bucketCounts[bucketIndex]);
            this._metrics.RecordDiscard();
            return;
        }

        this._metrics.AddRetained(array.Length);
        this._buckets[bucketIndex].Enqueue(array);
    }

    private void ReturnHuge(byte[] array)
    {
        // Check limit
        if (Interlocked.Increment(ref this._hugeCount) > MaxHugeArrays)
        {
            Interlocked.Decrement(ref this._hugeCount);
            this._metrics.RecordDiscard();
            return;
        }

        this._metrics.AddRetained(array.Length);
        this._hugePool.Enqueue(array);
    }

    private static int GetBucketIndex(int size)
    {
        for (var i = 0; i < s_buckets.Length; i++)
        {
            if (size <= s_buckets[i].Size)
                return i;
        }
        return -1;
    }

    public sealed class PoolMetrics
    {
        private long _totalRents;
        private long _hits;
        private long _misses;
        private long _discards;
        private long _currentRetainedBytes;
        private long _peakRetainedBytes;

        public long TotalRents => Interlocked.Read(ref this._totalRents);
        public long Hits => Interlocked.Read(ref this._hits);
        public long Misses => Interlocked.Read(ref this._misses);
        public long Discards => Interlocked.Read(ref this._discards);
        public long CurrentRetainedBytes => Interlocked.Read(ref this._currentRetainedBytes);
        public long PeakRetainedBytes => Interlocked.Read(ref this._peakRetainedBytes);

        // Hit rate among poolable (LOH) arrays only
        public double HitRate => this.Hits + this.Misses == 0 ? 0 : (double)this.Hits / (this.Hits + this.Misses);

        // Percentage of total rents that were pool hits
        public double PooledRentRate => this.TotalRents == 0 ? 0 : (double)this.Hits / this.TotalRents;

        internal void RecordRent() => Interlocked.Increment(ref this._totalRents);
        internal void RecordHit() => Interlocked.Increment(ref this._hits);
        internal void RecordMiss() => Interlocked.Increment(ref this._misses);
        internal void RecordDiscard() => Interlocked.Increment(ref this._discards);

        internal void AddRetained(long bytes)
        {
            var current = Interlocked.Add(ref this._currentRetainedBytes, bytes);
            while (current > Interlocked.Read(ref this._peakRetainedBytes))
                Interlocked.CompareExchange(ref this._peakRetainedBytes, current, this._peakRetainedBytes);
        }

        internal void RemoveRetained(long bytes)
        {
            Interlocked.Add(ref this._currentRetainedBytes, -bytes);
        }

        public void Reset()
        {
            Interlocked.Exchange(ref this._totalRents, 0);
            Interlocked.Exchange(ref this._hits, 0);
            Interlocked.Exchange(ref this._misses, 0);
            Interlocked.Exchange(ref this._discards, 0);
            Interlocked.Exchange(ref this._peakRetainedBytes, 0);
        }
    }
}

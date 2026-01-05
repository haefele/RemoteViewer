using Microsoft.Extensions.Time.Testing;
using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.Tests.Services;

public class BandwidthTrackerTests
{
    [Test]
    public async Task NewTrackerReturnsZeroBytesPerSecond()
    {
        var tracker = new BandwidthTracker();

        var result = tracker.GetBytesPerSecond();

        await Assert.That(result).IsEqualTo(0);
    }

    [Test]
    public async Task AddBytesIncreasesTotal()
    {
        var tracker = new BandwidthTracker();

        tracker.AddBytes(1000);
        tracker.AddBytes(2000);
        tracker.AddBytes(3000);

        // Total is 6000 bytes, divided by 5 second window = 1200 bytes/sec
        // But since all samples are within the window, we get sum/5
        var result = tracker.GetBytesPerSecond();

        await Assert.That(result).IsGreaterThan(0);
    }

    [Test]
    public async Task GetFormattedReturnsFormattedString()
    {
        var tracker = new BandwidthTracker();

        var result = tracker.GetFormatted();

        await Assert.That(result).StartsWith("Bandwidth:");
    }

    [Test]
    public async Task GetFormattedWithNoBytesReturnsZero()
    {
        var tracker = new BandwidthTracker();

        var result = tracker.GetFormatted();

        await Assert.That(result).IsEqualTo("Bandwidth: 0 B/s");
    }

    [Test]
    public async Task GetFormattedWithKilobytesShowsKBps()
    {
        var tracker = new BandwidthTracker();

        // Add 50KB worth of data (will be divided by 5 = 10KB/s)
        tracker.AddBytes(50_000);

        var result = tracker.GetFormatted();

        await Assert.That(result).Contains("KB/s");
    }

    [Test]
    public async Task GetFormattedWithMegabytesShowsMBps()
    {
        var tracker = new BandwidthTracker();

        // Add 50MB worth of data (will be divided by 5 = 10MB/s)
        tracker.AddBytes(50_000_000);

        var result = tracker.GetFormatted();

        await Assert.That(result).Contains("MB/s");
    }

    [Test]
    public async Task MultipleSamplesAccumulateCorrectly()
    {
        var fakeTime = new FakeTimeProvider();
        var tracker = new BandwidthTracker(fakeTime);

        // Add 5 samples of 1000 bytes each
        for (var i = 0; i < 5; i++)
        {
            tracker.AddBytes(1000);
        }

        // Total is 5000 bytes, divided by 5 second window = 1000 bytes/sec
        var result = tracker.GetBytesPerSecond();

        await Assert.That(result).IsEqualTo(1000);
    }

    [Test]
    public async Task TrackerIsThreadSafe()
    {
        var tracker = new BandwidthTracker();
        var tasks = new List<Task>();

        // Spawn multiple tasks to add bytes concurrently
        for (var i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (var j = 0; j < 100; j++)
                {
                    tracker.AddBytes(100);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Should have added 10 * 100 * 100 = 100,000 bytes total
        // Divided by 5 second window = 20,000 bytes/sec
        var result = tracker.GetBytesPerSecond();

        await Assert.That(result).IsEqualTo(20_000);
    }

    [Test]
    [Arguments(500, "B/s")]
    [Arguments(5_000, "KB/s")]
    [Arguments(50_000_000, "MB/s")]
    public async Task GetFormattedUsesCorrectUnit(int bytesToAdd, string expectedUnit)
    {
        var tracker = new BandwidthTracker();
        tracker.AddBytes(bytesToAdd);

        var result = tracker.GetFormatted();

        await Assert.That(result).Contains(expectedUnit);
    }

    [Test]
    public async Task OldSamplesArePrunedAfterWindowExpires()
    {
        var fakeTime = new FakeTimeProvider();
        var tracker = new BandwidthTracker(fakeTime);

        // Add bytes at time 0
        tracker.AddBytes(5000);

        // Verify bytes are counted
        var before = tracker.GetBytesPerSecond();
        await Assert.That(before).IsEqualTo(1000); // 5000 / 5 sec window

        // Advance time past the 5-second window
        fakeTime.Advance(TimeSpan.FromSeconds(6));

        // Old samples should be pruned
        var after = tracker.GetBytesPerSecond();
        await Assert.That(after).IsEqualTo(0);
    }

    [Test]
    public async Task SamplesWithinWindowAreKept()
    {
        var fakeTime = new FakeTimeProvider();
        var tracker = new BandwidthTracker(fakeTime);

        // Add bytes at time 0
        tracker.AddBytes(5000);

        // Advance 3 seconds (still within 5-second window)
        fakeTime.Advance(TimeSpan.FromSeconds(3));

        // Add more bytes
        tracker.AddBytes(5000);

        // Both samples should be counted: 10000 / 5 = 2000
        var result = tracker.GetBytesPerSecond();
        await Assert.That(result).IsEqualTo(2000);
    }

    [Test]
    public async Task PartialPruningKeepsRecentSamples()
    {
        var fakeTime = new FakeTimeProvider();
        var tracker = new BandwidthTracker(fakeTime);

        // Add first sample at time 0
        tracker.AddBytes(5000);

        // Advance 3 seconds
        fakeTime.Advance(TimeSpan.FromSeconds(3));

        // Add second sample at time 3
        tracker.AddBytes(10000);

        // Advance 3 more seconds (total 6 seconds from start)
        // First sample is now 6 seconds old (outside window)
        // Second sample is 3 seconds old (inside window)
        fakeTime.Advance(TimeSpan.FromSeconds(3));

        // Only second sample should remain: 10000 / 5 = 2000
        var result = tracker.GetBytesPerSecond();
        await Assert.That(result).IsEqualTo(2000);
    }

    [Test]
    public async Task RollingWindowCalculatesCorrectRate()
    {
        var fakeTime = new FakeTimeProvider();
        var tracker = new BandwidthTracker(fakeTime);

        // Simulate 1000 bytes per second for 3 seconds
        for (var i = 0; i < 3; i++)
        {
            tracker.AddBytes(1000);
            fakeTime.Advance(TimeSpan.FromSeconds(1));
        }

        // Should have 3000 bytes over 5-second window = 600 bytes/sec
        var result = tracker.GetBytesPerSecond();
        await Assert.That(result).IsEqualTo(600);

        // Add 2 more seconds of data
        for (var i = 0; i < 2; i++)
        {
            tracker.AddBytes(1000);
            fakeTime.Advance(TimeSpan.FromSeconds(1));
        }

        // Now have 5000 bytes over 5-second window = 1000 bytes/sec
        result = tracker.GetBytesPerSecond();
        await Assert.That(result).IsEqualTo(1000);
    }
}

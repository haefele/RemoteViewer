using System.Globalization;
using RemoteViewer.Client.Services;

namespace RemoteViewer.Client.Tests.Services;

public class BandwidthEstimatorTests
{
    [Test]
    [Arguments(1920, 1080, 1, 100, "Est. bandwidth:")]
    [Arguments(1920, 1080, 60, 100, "Est. bandwidth:")]
    [Arguments(3840, 2160, 30, 80, "Est. bandwidth:")]
    public async Task CalculateReturnsFormattedString(int width, int height, int fps, int quality, string expectedPrefix)
    {
        var result = BandwidthEstimator.Calculate(width, height, fps, quality);

        await Assert.That(result).StartsWith(expectedPrefix);
    }

    [Test]
    public async Task CalculateHighQualityHighFpsReturnsMBps()
    {
        // 1920x1080 @ 60fps, quality 100 should be in MB/s range
        var result = BandwidthEstimator.Calculate(1920, 1080, 60, 100);

        await Assert.That(result).Contains("MB/s");
    }

    [Test]
    public async Task CalculateLowQualityLowFpsReturnsKBps()
    {
        // 800x600 @ 1fps, quality 10 should be in KB/s range
        var result = BandwidthEstimator.Calculate(800, 600, 1, 10);

        await Assert.That(result).Contains("KB/s");
    }

    [Test]
    public async Task CalculateHigherQualityIncreasesEstimate()
    {
        var lowQuality = BandwidthEstimator.Calculate(1920, 1080, 30, 20);
        var highQuality = BandwidthEstimator.Calculate(1920, 1080, 30, 100);

        // Extract numeric values (rough check - high quality should have larger number)
        var lowValue = ExtractNumericValue(lowQuality);
        var highValue = ExtractNumericValue(highQuality);

        await Assert.That(highValue).IsGreaterThan(lowValue);
    }

    [Test]
    public async Task CalculateHigherFpsIncreasesEstimate()
    {
        var lowFps = BandwidthEstimator.Calculate(1920, 1080, 5, 80);
        var highFps = BandwidthEstimator.Calculate(1920, 1080, 60, 80);

        var lowValue = ExtractNumericValue(lowFps);
        var highValue = ExtractNumericValue(highFps);

        await Assert.That(highValue).IsGreaterThan(lowValue);
    }

    [Test]
    public async Task CalculateLargerScreenIncreasesEstimate()
    {
        var smallScreen = BandwidthEstimator.Calculate(1280, 720, 30, 80);
        var largeScreen = BandwidthEstimator.Calculate(3840, 2160, 30, 80);

        var smallValue = ExtractNumericValue(smallScreen);
        var largeValue = ExtractNumericValue(largeScreen);

        await Assert.That(largeValue).IsGreaterThan(smallValue);
    }

    [Test]
    [Arguments(1, 10)]
    [Arguments(60, 100)]
    [Arguments(30, 50)]
    public async Task CalculateDoesNotThrowForValidInputs(int fps, int quality)
    {
        var result = BandwidthEstimator.Calculate(1920, 1080, fps, quality);

        await Assert.That(result).IsNotNull();
        await Assert.That(result).IsNotEmpty();
    }

    private static double ExtractNumericValue(string formatted)
    {
        // Extract the numeric part and unit from strings like "Est. bandwidth: ~1.5 MB/s" or "Est. bandwidth: ~1,5 KB/s"
        // Must start with a digit to avoid matching "." in "Est."
        var match = System.Text.RegularExpressions.Regex.Match(formatted, @"~?(\d[\d.,]*)\s*(MB|KB|B)/s");
        if (!match.Success) return 0;

        // Handle both . and , as decimal separators
        var numStr = match.Groups[1].Value.Replace(',', '.');
        var value = double.Parse(numStr, CultureInfo.InvariantCulture);

        // Normalize to bytes for comparison
        var unit = match.Groups[2].Value;
        return unit switch
        {
            "MB" => value * 1_000_000,
            "KB" => value * 1_000,
            _ => value
        };
    }
}

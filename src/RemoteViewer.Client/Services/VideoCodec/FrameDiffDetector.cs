using System.Drawing;

namespace RemoteViewer.Client.Services.VideoCodec;

/// <summary>
/// Utility class for detecting differences between two frames by comparing 64x64 pixel blocks.
/// </summary>
public static class FrameDiffDetector
{
    public const int BlockSize = 32;

    /// <summary>
    /// Compares two frames and returns rectangles for changed 64x64 blocks.
    /// Returns null if more than 50% of blocks changed (caller should send keyframe instead).
    /// Returns empty array if no changes detected.
    /// </summary>
    /// <param name="currentPixels">Current frame pixel data in BGRA format</param>
    /// <param name="previousPixels">Previous frame pixel data in BGRA format</param>
    /// <param name="width">Frame width in pixels</param>
    /// <param name="height">Frame height in pixels</param>
    /// <param name="stride">Row stride in bytes (width * 4 for BGRA)</param>
    /// <returns>Array of changed rectangles, null if threshold exceeded, empty if no changes</returns>
    public static Rectangle[]? DetectChanges(
        ReadOnlySpan<byte> currentPixels,
        ReadOnlySpan<byte> previousPixels,
        int width,
        int height,
        int stride)
    {
        // Calculate total blocks for threshold check
        var blocksX = (width + BlockSize - 1) / BlockSize;
        var blocksY = (height + BlockSize - 1) / BlockSize;
        var totalBlocks = blocksX * blocksY;
        var threshold = totalBlocks * 0.8; // 80% threshold

        var changedBlocks = new List<Rectangle>();

        for (var blockY = 0; blockY < height; blockY += BlockSize)
        {
            for (var blockX = 0; blockX < width; blockX += BlockSize)
            {
                var maxX = Math.Min(blockX + BlockSize, width);
                var maxY = Math.Min(blockY + BlockSize, height);
                var rowWidthBytes = (maxX - blockX) * 4;

                var isChanged = false;
                for (var y = blockY; y < maxY && !isChanged; y++)
                {
                    var rowOffset = y * stride + blockX * 4;

                    var currentSpan = currentPixels.Slice(rowOffset, rowWidthBytes);
                    var previousSpan = previousPixels.Slice(rowOffset, rowWidthBytes);

                    if (!currentSpan.SequenceEqual(previousSpan))
                    {
                        isChanged = true;
                    }
                }

                if (isChanged)
                {
                    changedBlocks.Add(new Rectangle(blockX, blockY, maxX - blockX, maxY - blockY));

                    // Early exit if threshold exceeded - caller should send keyframe
                    if (changedBlocks.Count > threshold)
                    {
                        return null;
                    }
                }
            }
        }

        if (changedBlocks.Count == 0)
        {
            return [];
        }

        return MergeAdjacentRectangles(changedBlocks);
    }

    /// <summary>
    /// Merges nearby rectangles into larger bounding rectangles using proximity-based clustering.
    /// Rectangles within the proximity threshold are grouped and merged.
    /// </summary>
    public static Rectangle[] MergeAdjacentRectangles(List<Rectangle> rectangles)
    {
        if (rectangles.Count == 0)
            return [];

        if (rectangles.Count == 1)
            return [rectangles[0]];

        // Use half block size as proximity threshold
        const int ProximityThreshold = BlockSize / 2;

        // Initialize union-find parent array
        var parent = new int[rectangles.Count];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;

        // Group rectangles that are within proximity threshold
        for (var i = 0; i < rectangles.Count; i++)
        {
            var inflatedI = Rectangle.Inflate(rectangles[i], ProximityThreshold, ProximityThreshold);

            for (var j = i + 1; j < rectangles.Count; j++)
            {
                var inflatedJ = Rectangle.Inflate(rectangles[j], ProximityThreshold, ProximityThreshold);

                if (inflatedI.IntersectsWith(inflatedJ))
                {
                    Union(parent, i, j);
                }
            }
        }

        // Group rectangles by their root and merge each group
        var groups = new Dictionary<int, Rectangle>();
        for (var i = 0; i < rectangles.Count; i++)
        {
            var root = Find(parent, i);
            if (groups.TryGetValue(root, out var existing))
            {
                groups[root] = Rectangle.Union(existing, rectangles[i]);
            }
            else
            {
                groups[root] = rectangles[i];
            }
        }

        return [.. groups.Values];
    }

    private static int Find(int[] parent, int i)
    {
        if (parent[i] != i)
            parent[i] = Find(parent, parent[i]); // Path compression
        return parent[i];
    }

    private static void Union(int[] parent, int i, int j)
    {
        var rootI = Find(parent, i);
        var rootJ = Find(parent, j);
        if (rootI != rootJ)
            parent[rootI] = rootJ;
    }
}

using System.Drawing;

namespace RemoteViewer.Client.Services;

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
    /// Merges adjacent rectangles into larger rectangles to reduce the number of regions to transfer.
    /// Two rectangles are considered adjacent if they share an edge (same row or column).
    /// </summary>
    public static Rectangle[] MergeAdjacentRectangles(List<Rectangle> rectangles)
    {
        if (rectangles.Count == 0)
        {
            return [];
        }

        var merged = new List<Rectangle>(rectangles);
        var didMerge = false;

        do
        {
            didMerge = false;

            for (var i = 0; i < merged.Count; i++)
            {
                for (var j = i + 1; j < merged.Count; j++)
                {
                    if (AreAdjacent(merged[i], merged[j]))
                    {
                        merged[i] = Rectangle.Union(merged[i], merged[j]);
                        merged.RemoveAt(j);
                        didMerge = true;
                        j--;
                    }
                }
            }
        } while (didMerge);

        return merged.ToArray();
    }

    /// <summary>
    /// Checks if two grid-aligned rectangles share an edge (not just a corner).
    /// </summary>
    private static bool AreAdjacent(Rectangle a, Rectangle b)
    {
        // Horizontally adjacent: same row, edges touch
        var horizontallyAdjacent = a.Top == b.Top && a.Bottom == b.Bottom &&
                                    (a.Right == b.Left || b.Right == a.Left);

        // Vertically adjacent: same column, edges touch
        var verticallyAdjacent = a.Left == b.Left && a.Right == b.Right &&
                                  (a.Bottom == b.Top || b.Bottom == a.Top);

        return horizontallyAdjacent || verticallyAdjacent;
    }
}

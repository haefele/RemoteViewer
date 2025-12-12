using System.Drawing;

namespace RemoteViewer.Client.Services.VideoCodec;

public sealed class FrameDiffDetector : IDisposable
{
    public const int BlockSize = 32;

    private byte[]? _previousFrame;
    private int _width;
    private int _height;

    public Rectangle[]? DetectChanges(
        ReadOnlySpan<byte> currentPixels,
        int width,
        int height,
        int stride)
    {
        // If dimensions changed, reset state and return null (force keyframe)
        if (width != this._width || height != this._height)
        {
            this._previousFrame = null;
            this._width = width;
            this._height = height;
        }

        // If no previous frame, store current and return null (force keyframe)
        if (this._previousFrame is null)
        {
            this._previousFrame = currentPixels.ToArray();
            return null;
        }

        // Detect changes
        var result = DetectChangesInternal(currentPixels, this._previousFrame, width, height, stride);

        // Update previous frame
        currentPixels.CopyTo(this._previousFrame);

        return result;
    }

    public void Reset()
    {
        this._previousFrame = null;
    }

    public void Dispose()
    {
        this._previousFrame = null;
    }

    private static Rectangle[]? DetectChangesInternal(
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

#if WINDOWS
using System.Drawing;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using RemoteViewer.Client.Common;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace RemoteViewer.Client.Services.Screenshot;

public sealed class BitBltScreenGrabber(ILogger<BitBltScreenGrabber> logger) : IScreenGrabber, IDisposable
{
    private const int BlockSize = 32;

    private readonly Dictionary<string, BitBltDisplayState> _displayStates = new();
    private readonly object _lock = new();

    public bool IsAvailable => OperatingSystem.IsWindows();
    public int Priority => 50;

    public unsafe GrabResult CaptureDisplay(Display display, bool forceKeyframe)
    {
        var width = display.Bounds.Width;
        var height = display.Bounds.Height;
        var bufferSize = width * height * 4;

        var sourceDC = HDC.Null;
        var memoryDC = HDC.Null;
        DeleteObjectSafeHandle? hBitmapHandle = null;
        HGDIOBJ hOldBitmap = default;

        try
        {
            if (width <= 0 || height <= 0)
            {
                logger.LogWarning("Invalid display dimensions: {Width}x{Height}", width, height);
                return new GrabResult(GrabStatus.Failure, null, null, null);
            }

            sourceDC = PInvoke.GetDC(HWND.Null);
            if (sourceDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to get source DC: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null, null, null);
            }

            memoryDC = PInvoke.CreateCompatibleDC(sourceDC);
            if (memoryDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to create compatible DC: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null, null, null);
            }

            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                }
            };

            hBitmapHandle = PInvoke.CreateDIBSection(
                memoryDC,
                &bitmapInfo,
                DIB_USAGE.DIB_RGB_COLORS,
                out var pBits,
                null,
                0);

            if (hBitmapHandle.IsInvalid || pBits == null)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to create DIB section: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null, null, null);
            }

            hOldBitmap = PInvoke.SelectObject(memoryDC, new HGDIOBJ(hBitmapHandle.DangerousGetHandle()));

            if (PInvoke.BitBlt(
                memoryDC,
                0,
                0,
                width,
                height,
                sourceDC,
                display.Bounds.Left,
                display.Bounds.Top,
                ROP_CODE.SRCCOPY) == false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("BitBlt failed: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null, null, null);
            }

            // Allocate RefCountedMemoryOwner and copy pixels
            var frameMemory = RefCountedMemoryOwner<byte>.Create(bufferSize);

            fixed (byte* destPtr = frameMemory.Span)
            {
                Buffer.MemoryCopy(pBits, destPtr, bufferSize, bufferSize);
            }

            // Apply software diff detection
            return this.ApplyDiffDetection(display.Name, frameMemory, width, height, forceKeyframe);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while capturing display {DisplayName}", display.Name);
            return new GrabResult(GrabStatus.Failure, null, null, null);
        }
        finally
        {
            if (!hOldBitmap.IsNull)
            {
                PInvoke.SelectObject(memoryDC, hOldBitmap);
            }

            hBitmapHandle?.Dispose();

            if (!memoryDC.IsNull)
            {
                PInvoke.DeleteDC(memoryDC);
            }

            if (!sourceDC.IsNull)
            {
                var releaseResult = PInvoke.ReleaseDC(HWND.Null, sourceDC);
                if (releaseResult == 0)
                {
                    logger.LogWarning("Failed to release DC");
                }
            }
        }
    }

    private GrabResult ApplyDiffDetection(
        string displayName,
        RefCountedMemoryOwner<byte> frameMemory,
        int width,
        int height,
        bool forceKeyframe)
    {
        var state = this.GetOrCreateDisplayState(displayName);
        var stride = width * 4;

        // If dimensions changed, reset state
        if (width != state.Width || height != state.Height)
        {
            state.PreviousFrame?.Dispose();
            state.PreviousFrame = null;
            state.Width = width;
            state.Height = height;
        }

        // If forceKeyframe or no previous frame, return full frame
        if (forceKeyframe || state.PreviousFrame is null)
        {
            state.PreviousFrame?.Dispose();
            frameMemory.AddRef();
            state.PreviousFrame = frameMemory;
            return new GrabResult(GrabStatus.Success, frameMemory, null, null);
        }

        // Detect changes
        var dirtyRects = DetectChanges(
            frameMemory.Span,
            state.PreviousFrame.Span,
            width,
            height,
            stride);

        // null = too many changes, send as keyframe
        if (dirtyRects is null)
        {
            state.PreviousFrame.Dispose();
            frameMemory.AddRef();
            state.PreviousFrame = frameMemory;
            return new GrabResult(GrabStatus.Success, frameMemory, null, null);
        }

        // Empty array = no changes
        if (dirtyRects.Length == 0)
        {
            frameMemory.Dispose();
            return new GrabResult(GrabStatus.NoChanges, null, null, null);
        }

        // Extract dirty regions from full frame
        var dirtyRegions = ExtractDirtyRegions(frameMemory.Span, dirtyRects, width);

        // Update previous frame
        state.PreviousFrame.Dispose();
        frameMemory.AddRef();
        state.PreviousFrame = frameMemory;

        frameMemory.Dispose();
        return new GrabResult(GrabStatus.Success, null, dirtyRegions, null);
    }

    private BitBltDisplayState GetOrCreateDisplayState(string displayName)
    {
        lock (this._lock)
        {
            if (!this._displayStates.TryGetValue(displayName, out var state))
            {
                state = new BitBltDisplayState();
                this._displayStates[displayName] = state;
            }
            return state;
        }
    }

    public void ResetDisplay(string displayName)
    {
        lock (this._lock)
        {
            if (this._displayStates.TryGetValue(displayName, out var state))
            {
                state.Dispose();
                this._displayStates.Remove(displayName);
            }
        }
    }

    public void Dispose()
    {
        lock (this._lock)
        {
            foreach (var state in this._displayStates.Values)
            {
                state.Dispose();
            }
            this._displayStates.Clear();
        }
    }

    private static Rectangle[]? DetectChanges(
        ReadOnlySpan<byte> currentPixels,
        ReadOnlySpan<byte> previousPixels,
        int width,
        int height,
        int stride)
    {
        var blocksX = (width + BlockSize - 1) / BlockSize;
        var blocksY = (height + BlockSize - 1) / BlockSize;
        var totalBlocks = blocksX * blocksY;
        var threshold = totalBlocks * 0.8;

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

    private static Rectangle[] MergeAdjacentRectangles(List<Rectangle> rectangles)
    {
        if (rectangles.Count == 0)
            return [];

        if (rectangles.Count == 1)
            return [rectangles[0]];

        const int ProximityThreshold = BlockSize / 2;

        var parent = new int[rectangles.Count];
        for (var i = 0; i < parent.Length; i++)
            parent[i] = i;

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
            parent[i] = Find(parent, parent[i]);
        return parent[i];
    }

    private static void Union(int[] parent, int i, int j)
    {
        var rootI = Find(parent, i);
        var rootJ = Find(parent, j);
        if (rootI != rootJ)
            parent[rootI] = rootJ;
    }

    private static DirtyRegion[] ExtractDirtyRegions(ReadOnlySpan<byte> fullFrame, Rectangle[] dirtyRects, int frameWidth)
    {
        var regions = new DirtyRegion[dirtyRects.Length];

        for (var i = 0; i < dirtyRects.Length; i++)
        {
            var rect = dirtyRects[i];
            var regionBufferSize = rect.Width * rect.Height * 4;
            var regionMemory = RefCountedMemoryOwner<byte>.Create(regionBufferSize);

            var regionSpan = regionMemory.Span;
            var srcStride = frameWidth * 4;
            var destStride = rect.Width * 4;

            for (var y = 0; y < rect.Height; y++)
            {
                var srcOffset = (rect.Y + y) * srcStride + rect.X * 4;
                var destOffset = y * destStride;
                fullFrame.Slice(srcOffset, destStride).CopyTo(regionSpan.Slice(destOffset, destStride));
            }

            regions[i] = new DirtyRegion(rect.X, rect.Y, rect.Width, rect.Height, regionMemory);
        }

        return regions;
    }

    private sealed class BitBltDisplayState : IDisposable
    {
        public RefCountedMemoryOwner<byte>? PreviousFrame;
        public int Width;
        public int Height;

        public void Dispose() => this.PreviousFrame?.Dispose();
    }
}
#endif

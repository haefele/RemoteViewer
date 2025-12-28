using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RemoteViewer.Client.Common;
using RemoteViewer.Server.SharedAPI.Protocol;
using TurboJpegWrapper;

namespace RemoteViewer.Client.Views.Viewer;

/// <summary>
/// Composites delta frames onto a base frame using WriteableBitmap.
/// Manages the frame buffer and applies keyframes and delta updates.
/// </summary>
public class FrameCompositor : IDisposable
{
    private readonly TJDecompressor _decompressor = new();
    private readonly Lock _decompressorLock = new();

    private WriteableBitmap? _canvas;
    private WriteableBitmap? _debugOverlay;
    private int _width;
    private int _height;
    private ulong _baseFrameNumber;
    private bool _disposed;

    /// <summary>
    /// Gets the current composited bitmap for display.
    /// </summary>
    public WriteableBitmap? Canvas => this._canvas;

    /// <summary>
    /// Gets the frame number of the last applied keyframe.
    /// </summary>
    public ulong BaseFrameNumber => this._baseFrameNumber;

    /// <summary>
    /// Whether the compositor has a valid canvas.
    /// </summary>
    public bool HasCanvas => this._canvas is not null;

    /// <summary>
    /// When enabled, draws red borders around dirty regions for debugging.
    /// </summary>
    public bool ShowDirtyRegionBorders { get; set; }

    /// <summary>
    /// Gets the debug overlay bitmap (null when ShowDirtyRegionBorders is false).
    /// </summary>
    public WriteableBitmap? DebugOverlay => this._debugOverlay;

    /// <summary>
    /// Applies a keyframe, replacing the entire canvas.
    /// </summary>
    /// <param name="regions">Frame regions (should be single full-frame region for keyframe)</param>
    /// <param name="width">Full frame width</param>
    /// <param name="height">Full frame height</param>
    /// <param name="frameNumber">Frame number</param>
    public void ApplyKeyframe(FrameRegion[] regions, ulong frameNumber)
    {
        if (regions.Length == 0)
            return;

        var width = regions[0].Width;
        var height = regions[0].Height;

        // Resize canvas if needed
        if (this._canvas is null || this._width != width || this._height != height)
        {
            this._canvas?.Dispose();
            this._canvas = new WriteableBitmap(
                new PixelSize(width, height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                AlphaFormat.Premul);
            this._width = width;
            this._height = height;
        }

        // Apply each region (typically just one for keyframes)
        foreach (var region in regions)
        {
            this.ApplyRegion(region);
        }

        this._baseFrameNumber = frameNumber;
    }

    /// <summary>
    /// Applies delta regions to the existing canvas.
    /// </summary>
    /// <param name="regions">Array of dirty regions to apply</param>
    /// <param name="frameNumber">Frame number</param>
    public void ApplyDeltaRegions(FrameRegion[] regions, ulong frameNumber)
    {
        if (this._canvas is null)
            return; // Need keyframe first

        foreach (var region in regions)
            this.ApplyRegion(region);

        this.UpdateDebugOverlay(regions);
    }

    /// <summary>
    /// Applies a single region to the canvas.
    /// </summary>
    private unsafe void ApplyRegion(FrameRegion region)
    {
        if (this._canvas is null)
            return;

        // Clamp region to canvas bounds first to avoid unnecessary work
        var regionX = Math.Max(0, region.X);
        var regionY = Math.Max(0, region.Y);
        var regionWidth = Math.Min(region.Width, this._width - regionX);
        var regionHeight = Math.Min(region.Height, this._height - regionY);

        if (regionWidth <= 0 || regionHeight <= 0)
            return;

        lock (this._decompressorLock)
        {
            // Calculate buffer size (4 bytes per pixel for BGRA)
            var bufferSize = region.Width * region.Height * 4;
            using var pixelBuffer = RefCountedMemoryOwner.Create(bufferSize);

            fixed (byte* jpegPtr = region.Data.Span)
            fixed (byte* outPtr = pixelBuffer.Span)
            {
                this._decompressor.Decompress(
                    (nint)jpegPtr,
                    (ulong)region.Data.Length,
                    (nint)outPtr,
                    bufferSize,
                    TJPixelFormat.BGRA,
                    TJFlags.FastDct,
                    out _,
                    out _,
                    out var srcStride);

                using (var framebuffer = this._canvas.Lock())
                {
                    var destStride = framebuffer.RowBytes;
                    var destBase = (byte*)framebuffer.Address;

                    // Copy region to canvas at correct position
                    for (var y = 0; y < regionHeight; y++)
                    {
                        var destRow = destBase + (regionY + y) * destStride + regionX * 4;
                        var srcRow = outPtr + y * srcStride;
                        Buffer.MemoryCopy(srcRow, destRow, regionWidth * 4, regionWidth * 4);
                    }
                }
            }
        }
    }

    private void UpdateDebugOverlay(FrameRegion[] regions)
    {
        if (this.ShowDirtyRegionBorders is false)
        {
            this._debugOverlay?.Dispose();
            this._debugOverlay = null;
            return;
        }

        // Create or resize overlay to match canvas
        if (this._debugOverlay is null ||
            this._debugOverlay.PixelSize.Width != this._width ||
            this._debugOverlay.PixelSize.Height != this._height)
        {
            this._debugOverlay?.Dispose();
            this._debugOverlay = new WriteableBitmap(
                new PixelSize(this._width, this._height),
                new Vector(96, 96),
                Avalonia.Platform.PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        // Clear overlay and draw borders
        this.ClearOverlay();
        foreach (var region in regions)
        {
            var regionX = Math.Max(0, region.X);
            var regionY = Math.Max(0, region.Y);
            var regionWidth = Math.Min(region.Width, this._width - regionX);
            var regionHeight = Math.Min(region.Height, this._height - regionY);
            this.DrawBorderOnOverlay(regionX, regionY, regionWidth, regionHeight);
        }
    }

    private unsafe void ClearOverlay()
    {
        if (this._debugOverlay is null)
            return;

        using var framebuffer = this._debugOverlay.Lock();
        var size = framebuffer.RowBytes * this._height;
        new Span<byte>((void*)framebuffer.Address, size).Clear();
    }

    private unsafe void DrawBorderOnOverlay(int x, int y, int width, int height)
    {
        if (this._debugOverlay is null || width < 4 || height < 4)
            return;

        const int BorderThickness = 2;
        const uint Red = 0xFFFF0000; // BGRA: Blue=0, Green=0, Red=255, Alpha=255

        using var framebuffer = this._debugOverlay.Lock();
        var destStride = framebuffer.RowBytes;
        var destBase = (byte*)framebuffer.Address;

        // Draw top and bottom horizontal lines
        for (var t = 0; t < BorderThickness; t++)
        {
            var topRow = (uint*)(destBase + (y + t) * destStride) + x;
            var bottomRow = (uint*)(destBase + (y + height - 1 - t) * destStride) + x;
            for (var i = 0; i < width; i++)
            {
                topRow[i] = Red;
                bottomRow[i] = Red;
            }
        }

        // Draw left and right vertical lines (excluding corners already drawn)
        for (var row = y + BorderThickness; row < y + height - BorderThickness; row++)
        {
            var rowPtr = (uint*)(destBase + row * destStride);
            for (var t = 0; t < BorderThickness; t++)
            {
                rowPtr[x + t] = Red;
                rowPtr[x + width - 1 - t] = Red;
            }
        }
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._decompressor.Dispose();
        this._canvas?.Dispose();
        this._canvas = null;
        this._debugOverlay?.Dispose();
        this._debugOverlay = null;
    }
}

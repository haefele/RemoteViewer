using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RemoteViewer.Server.SharedAPI.Protocol;
using SkiaSharp;

namespace RemoteViewer.Client.Services;

/// <summary>
/// Composites delta frames onto a base frame using WriteableBitmap.
/// Manages the frame buffer and applies keyframes and delta updates.
/// </summary>
public class FrameCompositor : IDisposable
{
    private WriteableBitmap? _canvas;
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
    /// Applies a keyframe, replacing the entire canvas.
    /// </summary>
    /// <param name="regions">Frame regions (should be single full-frame region for keyframe)</param>
    /// <param name="width">Full frame width</param>
    /// <param name="height">Full frame height</param>
    /// <param name="frameNumber">Frame number</param>
    public void ApplyKeyframe(FrameRegion[] regions, int width, int height, ulong frameNumber)
    {
        if (regions.Length == 0)
            return;

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
        {
            this.ApplyRegion(region);
        }
    }

    /// <summary>
    /// Applies a single region to the canvas.
    /// </summary>
    private unsafe void ApplyRegion(FrameRegion region)
    {
        if (this._canvas is null)
            return;

        // Decode region JPEG
        using var regionBitmap = SKBitmap.Decode(region.Data.Span);
        if (regionBitmap is null)
            return;

        using (var framebuffer = this._canvas.Lock())
        {
            var destStride = framebuffer.RowBytes;
            var destBase = (byte*)framebuffer.Address;
            var srcPixels = (byte*)regionBitmap.GetPixels();
            var srcStride = regionBitmap.RowBytes;

            // Clamp region to canvas bounds
            var regionX = Math.Max(0, region.X);
            var regionY = Math.Max(0, region.Y);
            var regionWidth = Math.Min(region.Width, this._width - regionX);
            var regionHeight = Math.Min(region.Height, this._height - regionY);

            if (regionWidth <= 0 || regionHeight <= 0)
                return;

            // Copy region to canvas at correct position
            for (var y = 0; y < regionHeight; y++)
            {
                var destRow = destBase + (regionY + y) * destStride + regionX * 4;
                var srcRow = srcPixels + y * srcStride;
                Buffer.MemoryCopy(srcRow, destRow, regionWidth * 4, regionWidth * 4);
            }
        }
    }

    public void Dispose()
    {
        if (this._disposed)
            return;

        this._disposed = true;
        this._canvas?.Dispose();
        this._canvas = null;
    }
}

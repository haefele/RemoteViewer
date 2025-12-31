using System.Buffers;
using System.Collections.Concurrent;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;
using TurboJpegWrapper;

namespace RemoteViewer.Client.Services.VideoCodec;

public sealed class TurboJpegFrameEncoder : IFrameEncoder
{
    private const int JpegQuality = 90;
    private const int MaxPoolSize = 4;

    private readonly ConcurrentBag<TJCompressor> _compressorPool = new();
    private bool _disposed;

    public (FrameCodec Codec, EncodedRegion[] Regions) ProcessFrame(
        GrabResult grabResult,
        int width,
        int height)
    {
        var compressor = this.RentCompressor();
        try
        {
            // Keyframe: encode full frame
            if (grabResult.FullFramePixels is not null)
            {
                var jpegData = EncodeJpeg(compressor, grabResult.FullFramePixels.Span, width, height);

                return (FrameCodec.Jpeg90, [new EncodedRegion(true, 0, 0, width, height, jpegData)]);
            }

            // Delta frame: encode each dirty region
            var regions = new EncodedRegion[grabResult.DirtyRegions!.Length];
            for (var i = 0; i < grabResult.DirtyRegions.Length; i++)
            {
                var dirty = grabResult.DirtyRegions[i];
                var jpegData = EncodeJpeg(compressor, dirty.Pixels.Span, dirty.Width, dirty.Height);

                regions[i] = new EncodedRegion(false, dirty.X, dirty.Y, dirty.Width, dirty.Height, jpegData);
            }

            return (FrameCodec.Jpeg90, regions);
        }
        finally
        {
            this.ReturnCompressor(compressor);
        }
    }

    private TJCompressor RentCompressor()
    {
        ObjectDisposedException.ThrowIf(this._disposed, this);

        if (this._compressorPool.TryTake(out var compressor))
            return compressor;

        return new TJCompressor();
    }

    private void ReturnCompressor(TJCompressor compressor)
    {
        if (this._disposed || this._compressorPool.Count >= MaxPoolSize)
        {
            compressor.Dispose();
            return;
        }

        this._compressorPool.Add(compressor);
    }

    private static RefCountedMemoryOwner EncodeJpeg(TJCompressor compressor, Span<byte> pixels, int width, int height)
    {
        // Get max possible JPEG size for this resolution
        var maxSize = compressor.GetBufferSize(width, height, TJSubsamplingOption.Chrominance420);

        // Allocate buffer with max size (ArrayPool will likely give us a larger array anyway)
        var memoryOwner = RefCountedMemoryOwner.Create(maxSize);

        // Encode directly into our buffer - returns slice with actual compressed size
        var result = compressor.Compress(
            pixels,
            memoryOwner.Span,
            width,
            height,
            TJPixelFormat.BGRA,
            TJSubsamplingOption.Chrominance420,
            JpegQuality,
            TJFlags.None);

        // Update to actual compressed size (no extra copy needed)
        memoryOwner.SetLength(result.Length);

        return memoryOwner;
    }

    public void Dispose()
    {
        this._disposed = true;

        while (this._compressorPool.TryTake(out var compressor))
        {
            compressor.Dispose();
        }
    }
}

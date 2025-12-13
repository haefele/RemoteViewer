using System.Buffers;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;
using TurboJpegWrapper;

namespace RemoteViewer.Client.Services.VideoCodec;

public sealed class ScreenEncoder : IDisposable
{
    private const int JpegQuality = 90;

    private readonly TJCompressor _compressor = new();

    public (FrameCodec Codec, EncodedRegion[] Regions) ProcessFrame(
        GrabResult grabResult,
        int width,
        int height)
    {
        // Keyframe: encode full frame
        if (grabResult.FullFramePixels is not null)
        {
            var jpegData = this.EncodeJpeg(grabResult.FullFramePixels.Span, width, height);

            return (FrameCodec.Jpeg90, [new EncodedRegion(true, 0, 0, width, height, jpegData)]);
        }

        // Delta frame: encode each dirty region
        var regions = new EncodedRegion[grabResult.DirtyRegions!.Length];
        for (var i = 0; i < grabResult.DirtyRegions.Length; i++)
        {
            var dirty = grabResult.DirtyRegions[i];
            var jpegData = this.EncodeJpeg(dirty.Pixels.Span, dirty.Width, dirty.Height);

            regions[i] = new EncodedRegion(false, dirty.X, dirty.Y, dirty.Width, dirty.Height, jpegData);
        }

        return (FrameCodec.Jpeg90, regions);
    }

    private IMemoryOwner<byte> EncodeJpeg(Span<byte> pixels, int width, int height)
    {
        // Get max possible JPEG size for this resolution
        var maxSize = this._compressor.GetBufferSize(width, height, TJSubsamplingOption.Chrominance420);

        // Allocate buffer with max size (ArrayPool will likely give us a larger array anyway)
        var memoryOwner = RefCountedMemoryOwner<byte>.Create(maxSize);

        // Encode directly into our buffer - returns slice with actual compressed size
        var result = this._compressor.Compress(
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
        this._compressor.Dispose();
    }
}

public readonly record struct EncodedRegion(
    bool IsKeyframe,
    int X,
    int Y,
    int Width,
    int Height,
    IMemoryOwner<byte> JpegData
);

using System.Buffers;
using System.Runtime.CompilerServices;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.ScreenCapture;
using RemoteViewer.Server.SharedAPI.Protocol;
using SkiaSharp;

namespace RemoteViewer.Client.Services.VideoCodec;

public sealed class ScreenEncoder
{
    private const int JpegQuality = 75;

    public EncodeResult ProcessFrame(
        GrabResult grabResult,
        int width,
        int height)
    {
        if (grabResult.Status != GrabStatus.Success)
        {
            return new EncodeResult(false, FrameType.DeltaFrame, []);
        }

        // Keyframe: encode full frame
        if (grabResult.FullFramePixels is not null)
        {
            var jpegData = this.EncodeJpeg(grabResult.FullFramePixels.Span, width, height);

            return new EncodeResult(
                true,
                FrameType.Keyframe,
                [new EncodedRegion(0, 0, width, height, jpegData)]);
        }

        // Delta frame: encode each dirty region (already compact pixel data)
        if (grabResult.DirtyRegions is { Length: > 0 })
        {
            var regions = new EncodedRegion[grabResult.DirtyRegions.Length];

            for (var i = 0; i < grabResult.DirtyRegions.Length; i++)
            {
                var dirty = grabResult.DirtyRegions[i];

                var jpegData = this.EncodeJpeg(dirty.Pixels.Span, dirty.Width, dirty.Height);

                regions[i] = new EncodedRegion(dirty.X, dirty.Y, dirty.Width, dirty.Height, jpegData);
            }

            return new EncodeResult(true, FrameType.DeltaFrame, regions);
        }

        // No data to encode
        return new EncodeResult(false, FrameType.DeltaFrame, []);
    }

    private unsafe IMemoryOwner<byte> EncodeJpeg(ReadOnlySpan<byte> pixels, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        var destPtr = (byte*)bitmap.GetPixels();
        var bufferSize = width * height * 4;

        fixed (byte* srcPtr = pixels)
        {
            Unsafe.CopyBlock(destPtr, srcPtr, (uint)bufferSize);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var jpegSpan = data.AsSpan();
        var memoryOwner = RefCountedMemoryOwner<byte>.Create(jpegSpan.Length);
        jpegSpan.CopyTo(memoryOwner.Span);

        return memoryOwner;
    }
}

public readonly record struct EncodeResult(
    bool HasChanges,
    FrameType FrameType,
    EncodedRegion[] Regions
);

public readonly record struct EncodedRegion(
    int X,
    int Y,
    int Width,
    int Height,
    IMemoryOwner<byte> JpegData
);

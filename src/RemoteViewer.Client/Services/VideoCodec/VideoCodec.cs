using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using RemoteViewer.Client.Common;
using RemoteViewer.Server.SharedAPI.Protocol;
using SkiaSharp;

namespace RemoteViewer.Client.Services.VideoCodec;

public sealed class ScreenEncoder : IDisposable
{
    private const int KeyframeIntervalMs = 3000;
    private const int JpegQuality = 75;

    private readonly Dictionary<string, DisplayCodecState> _displayStates = new();
    private readonly object _lock = new();

    public EncodeResult ProcessFrame(
        string displayName,
        IMemoryOwner<byte> frameBuffer,
        int width,
        int height,
        Rectangle[]? dxgiDirtyRects)
    {
        lock (this._lock)
        {
            var state = this.GetOrCreateState(displayName);

            if (state.Width != width || state.Height != height)
            {
                state.PreviousFrame?.Dispose();
                state.PreviousFrame = null;
                state.Width = width;
                state.Height = height;
                state.ForceNextKeyframe = true;
            }

            var keyframeDue = state.KeyframeTimer.ElapsedMilliseconds >= KeyframeIntervalMs || state.ForceNextKeyframe;

            Rectangle[]? dirtyRects;
            if (dxgiDirtyRects is { Length: > 0 })
            {
                dirtyRects = dxgiDirtyRects;
            }
            else if (state.PreviousFrame is not null && !keyframeDue)
            {
                var stride = width * 4;
                dirtyRects = FrameDiffDetector.DetectChanges(
                    frameBuffer.Memory.Span,
                    state.PreviousFrame.Memory.Span,
                    width,
                    height,
                    stride);
            }
            else
            {
                dirtyRects = null;
            }

            var isKeyframe = keyframeDue || dirtyRects is null;

            if (dirtyRects is { Length: 0 })
            {
                // Early return, we don't need this frameBuffer - make sure to dispose of it as ownership was passed to us
                frameBuffer.Dispose();
                return new EncodeResult(false, FrameType.DeltaFrame, []);
            }

            state.PreviousFrame?.Dispose();
            state.PreviousFrame = frameBuffer;

            if (isKeyframe)
            {
                state.KeyframeTimer.Restart();
                state.ForceNextKeyframe = false;
            }

            var regions = this.EncodeRegions(frameBuffer.Memory.Span, width, height, isKeyframe ? null : dirtyRects);

            return new EncodeResult(
                true,
                isKeyframe ? FrameType.Keyframe : FrameType.DeltaFrame,
                regions);
        }
    }

    public void RequestKeyframe(string displayName)
    {
        lock (this._lock)
        {
            var state = this.GetOrCreateState(displayName);
            state.ForceNextKeyframe = true;
        }
    }

    public void RemoveDisplay(string displayName)
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

    private DisplayCodecState GetOrCreateState(string displayName)
    {
        if (!this._displayStates.TryGetValue(displayName, out var state))
        {
            state = new DisplayCodecState();
            this._displayStates[displayName] = state;
        }
        return state;
    }

    private EncodedRegion[] EncodeRegions(ReadOnlySpan<byte> framePixels, int frameWidth, int frameHeight, Rectangle[]? dirtyRects)
    {
        if (dirtyRects is null)
        {
            var jpegData = this.EncodeJpegRegion(framePixels, 0, 0, frameWidth, frameHeight, frameWidth);
            return [new EncodedRegion(0, 0, frameWidth, frameHeight, jpegData)];
        }

        var regions = new EncodedRegion[dirtyRects.Length];

        for (var i = 0; i < dirtyRects.Length; i++)
        {
            var rect = dirtyRects[i];
            var jpegData = this.EncodeJpegRegion(framePixels, rect.X, rect.Y, rect.Width, rect.Height, frameWidth);
            regions[i] = new EncodedRegion(rect.X, rect.Y, rect.Width, rect.Height, jpegData);
        }

        return regions;
    }

    private unsafe IMemoryOwner<byte> EncodeJpegRegion(
        ReadOnlySpan<byte> framePixels,
        int x, int y, int regionWidth, int regionHeight,
        int frameWidth)
    {
        using var regionBitmap = new SKBitmap(regionWidth, regionHeight, SKColorType.Bgra8888, SKAlphaType.Premul);

        var destPtr = (byte*)regionBitmap.GetPixels();
        var regionStride = regionWidth * 4;
        var frameStride = frameWidth * 4;

        fixed (byte* srcBase = framePixels)
        {
            for (var row = 0; row < regionHeight; row++)
            {
                var srcRow = srcBase + (y + row) * frameStride + x * 4;
                var destRow = destPtr + row * regionStride;
                Unsafe.CopyBlock(destRow, srcRow, (uint)regionStride);
            }
        }

        using var image = SKImage.FromBitmap(regionBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);

        var jpegSpan = data.AsSpan();
        var memoryOwner = RefCountedMemoryOwner<byte>.Create(jpegSpan.Length);
        jpegSpan.CopyTo(memoryOwner.Span);

        return memoryOwner;
    }

    private sealed class DisplayCodecState : IDisposable
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public IMemoryOwner<byte>? PreviousFrame { get; set; }
        public Stopwatch KeyframeTimer { get; } = Stopwatch.StartNew();
        public bool ForceNextKeyframe { get; set; } = true;

        public void Dispose()
        {
            this.PreviousFrame?.Dispose();
            this.PreviousFrame = null;
        }
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

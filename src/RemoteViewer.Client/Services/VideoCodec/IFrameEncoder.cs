using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.VideoCodec;

public interface IFrameEncoder : IDisposable
{
    (FrameCodec Codec, EncodedRegion[] Regions) ProcessFrame(
        GrabResult grabResult,
        int width,
        int height);
}

public readonly record struct EncodedRegion(
    bool IsKeyframe,
    int X,
    int Y,
    int Width,
    int Height,
    RefCountedMemoryOwner JpegData
) : IDisposable
{
    public void Dispose()
    {
        this.JpegData.Dispose();
    }
}

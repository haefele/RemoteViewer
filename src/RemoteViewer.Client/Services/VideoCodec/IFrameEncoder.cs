using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.VideoCodec;

public interface IFrameEncoder : IDisposable
{
    int Quality { get; set; }

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

using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IViewerServiceImpl
{
    void HandleFrame(string displayId, ulong frameNumber, FrameCodec codec, FrameRegion[] regions);
}

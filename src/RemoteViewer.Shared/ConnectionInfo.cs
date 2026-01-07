using PolyType;

namespace RemoteViewer.Shared;

[GenerateSerializer]
public record ClientInfo([property: Id(0)] string ClientId, [property: Id(1)] string DisplayName);

[GenerateSerializer]
public record DisplayInfo(
    [property: Id(0)] string Id,
    [property: Id(1)] string FriendlyName,
    [property: Id(2)] bool IsPrimary,
    [property: Id(3)] int Left,
    [property: Id(4)] int Top,
    [property: Id(5)] int Right,
    [property: Id(6)] int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

[GenerateSerializer]
public record ConnectionProperties(
    [property: Id(0)] bool CanSendSecureAttentionSequence,
    [property: Id(1)] List<string> InputBlockedViewerIds,
    [property: Id(2)] List<DisplayInfo> AvailableDisplays);

[GenerateSerializer]
public record ConnectionInfo(
    [property: Id(0)] string ConnectionId,
    [property: Id(1)] ClientInfo Presenter,
    [property: Id(2)] List<ClientInfo> Viewers,
    [property: Id(3)] ConnectionProperties Properties);

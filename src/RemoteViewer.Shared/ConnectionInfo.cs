namespace RemoteViewer.Shared;

public record ClientInfo(string ClientId, string DisplayName);

public record DisplayInfo(
    string Id,
    string FriendlyName,
    bool IsPrimary,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => this.Right - this.Left;
    public int Height => this.Bottom - this.Top;
}

public record ConnectionProperties(
    bool CanSendSecureAttentionSequence,
    List<string> InputBlockedViewerIds,
    List<DisplayInfo> AvailableDisplays);

public record ConnectionInfo(
    string ConnectionId,
    ClientInfo Presenter,
    List<ClientInfo> Viewers,
    ConnectionProperties Properties);

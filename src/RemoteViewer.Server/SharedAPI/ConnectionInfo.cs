using PolyType;

namespace RemoteViewer.Server.SharedAPI;

public record ClientInfo(string ClientId, string DisplayName);
public record ConnectionProperties(bool CanSendSecureAttentionSequence, List<string> InputBlockedViewerIds);
public record ConnectionInfo(string ConnectionId, ClientInfo Presenter, List<ClientInfo> Viewers, ConnectionProperties Properties);

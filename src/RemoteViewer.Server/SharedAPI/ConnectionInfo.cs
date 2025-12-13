using PolyType;

namespace RemoteViewer.Server.SharedAPI;

public record ClientInfo(string ClientId, string DisplayName);

public record ConnectionInfo(string ConnectionId, ClientInfo Presenter, List<ClientInfo> Viewers);

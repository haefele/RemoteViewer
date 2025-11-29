using PolyType;

namespace RemoteViewer.Server.SharedAPI;

public record ConnectionInfo(string ConnectionId, string PresenterClientId, List<string> ViewerClientIds);

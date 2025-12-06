namespace RemoteViewer.Server.Hubs;

internal static partial class ConnectionHubLogs
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Client version mismatch. ClientVersion: {ClientVersion}, ServerVersion: {ServerVersion}, ConnectionId: {ConnectionId}")]
    public static partial void VersionMismatch(this ILogger logger, string? clientVersion, string serverVersion, string connectionId);
}

using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Server.Services;

internal static partial class ConnectionsServiceLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Client registration started for SignalR connection: {SignalRConnectionId}")]
    public static partial void ClientRegistrationStarted(this ILogger logger, string signalRConnectionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Username collision detected on attempt {Attempt}, retrying")]
    public static partial void UsernameCollision(this ILogger logger, int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client registered successfully. ClientId: {ClientId}, Username: {Username}, TotalClients: {TotalClients}")]
    public static partial void ClientRegistered(this ILogger logger, string clientId, string username, int totalClients);
    [LoggerMessage(Level = LogLevel.Information, Message = "Client password changed successfully. ClientId: {ClientId}")]
    public static partial void ClientPasswordChanged(this ILogger logger, string clientId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Client registration required {Attempts} attempts due to username collisions")]
    public static partial void MultipleRegistrationAttempts(this ILogger logger, int attempts);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client unregistration started for SignalR connection: {SignalRConnectionId}")]
    public static partial void ClientUnregistrationStarted(this ILogger logger, string signalRConnectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection stopped due to presenter disconnect. ConnectionId: {ConnectionId}")]
    public static partial void ConnectionStoppedPresenterDisconnect(this ILogger logger, string connectionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client unregistered. SignalR: {SignalRConnectionId}, ClientsRemoved: {ClientsRemoved}, ConnectionsRemoved: {ConnectionsRemoved}, RemainingClients: {RemainingClients}, RemainingConnections: {RemainingConnections}")]
    public static partial void ClientUnregistered(this ILogger logger, string signalRConnectionId, int clientsRemoved, int connectionsRemoved, int remainingClients, int remainingConnections);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection attempt started. ViewerConnectionId: {ConnectionId}, Username: {Username}")]
    public static partial void ConnectionAttemptStarted(this ILogger logger, string connectionId, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection attempt failed: Viewer not found. Client SignalRConnectionId: {SignalRConnectionId}")]
    public static partial void ViewerNotFound(this ILogger logger, string signalRConnectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection attempt failed: Incorrect credentials. ClientId: {ClientId}, Username: {Username}")]
    public static partial void IncorrectCredentials(this ILogger logger, string clientId, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection attempt failed: Cannot connect to yourself. ClientId: {ClientId}")]
    public static partial void CannotConnectToYourself(this ILogger logger, string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "New connection created. ConnectionId: {ConnectionId}, PresenterId: {PresenterId}, ViewerId: {ViewerId}, TotalConnections: {TotalConnections}")]
    public static partial void NewConnectionCreated(this ILogger logger, string connectionId, string presenterId, string viewerId, int totalConnections);

    [LoggerMessage(Level = LogLevel.Information, Message = "Adding viewer to connection. ConnectionId: {ConnectionId}, ViewerId: {ViewerId}")]
    public static partial void AddingViewerToConnection(this ILogger logger, string connectionId, string viewerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection attempt succeeded. Username: {Username}")]
    public static partial void ConnectionAttemptSucceeded(this ILogger logger, string username);


    [LoggerMessage(Level = LogLevel.Debug, Message = "Connection created for presenter. ConnectionId: {ConnectionId}, PresenterId: {PresenterId}")]
    public static partial void ConnectionCreated(this ILogger logger, string connectionId, string presenterId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Viewer added to connection. ConnectionId: {ConnectionId}, ViewerId: {ViewerId}, TotalViewers: {TotalViewers}")]
    public static partial void ViewerAdded(this ILogger logger, string connectionId, string viewerId, int totalViewers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Viewer already present in connection. ConnectionId: {ConnectionId}, ViewerId: {ViewerId}")]
    public static partial void ViewerAlreadyPresent(this ILogger logger, string connectionId, string viewerId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Presenter disconnected from connection. ConnectionId: {ConnectionId}, PresenterId: {PresenterId}, NotifyingViewers: {ViewerCount}")]
    public static partial void PresenterDisconnected(this ILogger logger, string connectionId, string presenterId, int viewerCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Viewer disconnected from connection. ConnectionId: {ConnectionId}, RemovedViewers: {RemovedCount}, RemainingViewers: {RemainingViewers}")]
    public static partial void ViewerDisconnected(this ILogger logger, string connectionId, int removedCount, int remainingViewers);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcasting connection state change. ConnectionId: {ConnectionId}, ViewerCount: {ViewerCount}")]
    public static partial void ConnectionStateChange(this ILogger logger, string connectionId, int viewerCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message send started. ClientId: {ClientId}, ConnectionId: {ConnectionId}, MessageType: {MessageType}, Destination: {Destination}, Size: {DataSize} bytes")]
    public static partial void MessageSendStarted(this ILogger logger, string clientId, string connectionId, string messageType, MessageDestination destination, int dataSize);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message sender not found. SignalR: {SignalRConnectionId}")]
    public static partial void MessageSenderNotFound(this ILogger logger, string signalRConnectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message connection not found. ConnectionId: {ConnectionId}")]
    public static partial void MessageConnectionNotFound(this ILogger logger, string connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Message sender not in connection. ClientId: {ClientId}, ConnectionId: {ConnectionId}")]
    public static partial void MessageSenderNotInConnection(this ILogger logger, string clientId, string connectionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message sent to presenter. ConnectionId: {ConnectionId}, SenderId: {SenderId}, PresenterId: {PresenterId}")]
    public static partial void MessageSentToPresenter(this ILogger logger, string connectionId, string senderId, string presenterId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message sent to viewers. ConnectionId: {ConnectionId}, SenderId: {SenderId}, ViewerCount: {ViewerCount}")]
    public static partial void MessageSentToViewers(this ILogger logger, string connectionId, string senderId, int viewerCount);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message sent to all participants. ConnectionId: {ConnectionId}, SenderId: {SenderId}, RecipientCount: {RecipientCount}")]
    public static partial void MessageSentToAll(this ILogger logger, string connectionId, string senderId, int recipientCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Message send completed. ClientId: {ClientId}, ConnectionId: {ConnectionId}, MessageType: {MessageType}")]
    public static partial void MessageSendCompleted(this ILogger logger, string clientId, string connectionId, string messageType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Message sent to specific clients. ConnectionId: {ConnectionId}, SenderId: {SenderId}, SentCount: {SentCount}, RequestedCount: {RequestedCount}")]
    public static partial void MessageSentToSpecificClients(this ILogger logger, string connectionId, string senderId, int sentCount, int requestedCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnect from connection started. SignalRConnectionId: {SignalRConnectionId}, ConnectionId: {ConnectionId}")]
    public static partial void DisconnectFromConnectionStarted(this ILogger logger, string signalRConnectionId, string connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnect failed: Connection not found. ConnectionId: {ConnectionId}")]
    public static partial void DisconnectConnectionNotFound(this ILogger logger, string connectionId);
}
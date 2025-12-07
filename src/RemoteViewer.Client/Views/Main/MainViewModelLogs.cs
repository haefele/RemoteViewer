using Microsoft.Extensions.Logging;

namespace RemoteViewer.Client.Views.Main;

internal static partial class MainViewModelLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connecting to device: {Username}")]
    public static partial void ConnectingToDevice(this ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connection failed: {Error}")]
    public static partial void ConnectionFailed(this ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection successful, opening {WindowType} window")]
    public static partial void ConnectionSuccessful(this ILogger logger, string windowType);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Credentials assigned: {Username}")]
    public static partial void CredentialsAssigned(this ILogger logger, string username);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Copied credentials to clipboard")]
    public static partial void CopiedCredentialsToClipboard(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Generated new password")]
    public static partial void GeneratedNewPassword(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Hub connection status changed. IsConnected: {IsConnected}, Status: {StatusText}")]
    public static partial void HubConnectionStatusChanged(this ILogger logger, bool isConnected, string statusText);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Session window closed, showing main view")]
    public static partial void SessionWindowClosed(this ILogger logger);
}

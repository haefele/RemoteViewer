using Microsoft.Extensions.Logging;
namespace RemoteViewer.Server.Orleans.Grains;

public sealed partial class ClientSendGrain
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped frame {MessageType}")]
    partial void LogFrameDropped(string messageType);
}

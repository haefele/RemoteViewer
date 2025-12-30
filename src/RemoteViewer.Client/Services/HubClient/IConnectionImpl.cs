using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IConnectionImpl
{
    void OnMessageReceived(string senderClientId, string messageType, byte[] data);
    void OnConnectionChanged(ConnectionInfo connectionInfo);
    void OnClosed();
}

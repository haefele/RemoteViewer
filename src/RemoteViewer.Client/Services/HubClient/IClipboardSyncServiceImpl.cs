using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IClipboardSyncServiceImpl
{
    void HandleTextMessage(ClipboardTextMessage message);
    void HandleImageMessage(ClipboardImageMessage message);
}

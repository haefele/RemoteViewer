using RemoteViewer.Shared.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IClipboardSyncServiceImpl
{
    Task HandleTextMessageAsync(ClipboardTextMessage message);
    Task HandleImageMessageAsync(ClipboardImageMessage message);
}

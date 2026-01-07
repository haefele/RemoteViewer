using Orleans;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Grains.Interfaces;

public interface IConnectionGrain : IGrainWithStringKey
{
    Task InitializeAsync(string presenterSignalrId);
    Task<bool> AddViewerAsync(string viewerSignalrId);
    Task<bool> RemoveClientAsync(string signalrId);
    Task<bool> IsActiveAsync();
    Task<string?> GetPresenterSignalrIdAsync();
    Task<IReadOnlySet<string>> GetViewerSignalrIdsAsync();
    Task<ConnectionProperties> GetPropertiesAsync();
    Task<bool> UpdatePropertiesAsync(string requestingSignalrId, ConnectionProperties properties);
    Task<bool> IsPresenterAsync(string signalrId);
    Task<bool> SendMessageAsync(string senderSignalrId, string messageType, byte[] data, MessageDestination destination, IReadOnlyList<string>? targetClientIds);
}

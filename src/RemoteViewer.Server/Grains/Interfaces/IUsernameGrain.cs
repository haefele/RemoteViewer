using Orleans;

namespace RemoteViewer.Server.Grains.Interfaces;

public interface IUsernameGrain : IGrainWithStringKey
{
    Task<bool> TryClaimAsync(string signalrConnectionId);
    Task<string?> GetSignalrConnectionIdAsync();
    Task ReleaseAsync(string signalrConnectionId);
}

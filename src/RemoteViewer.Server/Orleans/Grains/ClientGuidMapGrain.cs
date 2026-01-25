using Orleans.Concurrency;

namespace RemoteViewer.Server.Orleans.Grains;

public interface IClientGuidMapGrain : IGrainWithStringKey
{
    Task SetSignalrConnectionIdAsync(string signalrConnectionId);
    Task<string?> GetSignalrConnectionIdAsync();
    Task ClearSignalrConnectionIdAsync(string signalrConnectionId);
}

public sealed class ClientGuidMapGrain : Grain, IClientGuidMapGrain
{
    private string? _signalrConnectionId;

    public Task SetSignalrConnectionIdAsync(string signalrConnectionId)
    {
        this._signalrConnectionId = signalrConnectionId;
        return Task.CompletedTask;
    }

    [ReadOnly]
    public Task<string?> GetSignalrConnectionIdAsync()
    {
        return Task.FromResult(this._signalrConnectionId);
    }

    public Task ClearSignalrConnectionIdAsync(string signalrConnectionId)
    {
        if (string.Equals(this._signalrConnectionId, signalrConnectionId, StringComparison.Ordinal))
        {
            this._signalrConnectionId = null;
        }

        return Task.CompletedTask;
    }
}

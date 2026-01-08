using Orleans;
using Orleans.Concurrency;

namespace RemoteViewer.Server.Grains;

public interface IUsernameGrain : IGrainWithStringKey
{
    Task<bool> TryClaimAsync(string signalrConnectionId);
    [ReadOnly]
    Task<string?> GetSignalrConnectionIdAsync();
    Task ReleaseAsync(string signalrConnectionId);
}

public sealed class UsernameGrain : Grain, IUsernameGrain
{
    private string? _ownerSignalrId;

    public Task<bool> TryClaimAsync(string signalrConnectionId)
    {
        if (this._ownerSignalrId is not null)
            return Task.FromResult(false);

        this._ownerSignalrId = signalrConnectionId;
        return Task.FromResult(true);
    }

    public Task<string?> GetSignalrConnectionIdAsync()
    {
        return Task.FromResult(this._ownerSignalrId);
    }

    public Task ReleaseAsync(string signalrConnectionId)
    {
        if (this._ownerSignalrId == signalrConnectionId)
        {
            this._ownerSignalrId = null;
            this.DeactivateOnIdle();
        }

        return Task.CompletedTask;
    }
}

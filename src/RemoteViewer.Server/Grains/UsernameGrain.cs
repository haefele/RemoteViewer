using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Server.Grains.Interfaces;

namespace RemoteViewer.Server.Grains;

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

    [ReadOnly]
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

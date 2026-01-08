using Orleans;
using Orleans.Concurrency;

namespace RemoteViewer.Server.Orleans.Grains;

public interface IUsernameGrain : IGrainWithStringKey
{
    Task<bool> TryClaimAsync(string signalrConnectionId);
    [ReadOnly]
    Task<string?> GetSignalrConnectionIdAsync();
    Task ReleaseAsync(string signalrConnectionId);
}

public sealed partial class UsernameGrain(ILogger<UsernameGrain> logger) : Grain, IUsernameGrain
{
    private string? _ownerSignalrId;

    public Task<bool> TryClaimAsync(string signalrConnectionId)
    {
        if (this._ownerSignalrId is not null)
        {
            this.LogUsernameAlreadyClaimed(this.GetPrimaryKeyString());
            return Task.FromResult(false);
        }

        this._ownerSignalrId = signalrConnectionId;
        this.LogUsernameClaimed(this.GetPrimaryKeyString(), signalrConnectionId);
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
            this.LogUsernameReleased(this.GetPrimaryKeyString(), signalrConnectionId);
            this._ownerSignalrId = null;
            this.DeactivateOnIdle();
        }
        else
        {
            this.LogUsernameReleaseNotOwner(this.GetPrimaryKeyString(), signalrConnectionId);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Username {Username} claimed by {SignalrId}")]
    private partial void LogUsernameClaimed(string username, string signalrId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Username {Username} already claimed")]
    private partial void LogUsernameAlreadyClaimed(string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Username {Username} released by {SignalrId}")]
    private partial void LogUsernameReleased(string username, string signalrId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot release username {Username}: not owned by {SignalrId}")]
    private partial void LogUsernameReleaseNotOwner(string username, string signalrId);
}

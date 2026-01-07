using Orleans;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Grains.Interfaces;

public interface IClientGrain : IGrainWithStringKey
{
    Task<string> InitializeAsync(string? displayName);
    Task<string> GenerateNewPasswordAsync();
    Task SetDisplayNameAsync(string displayName);
    Task<bool> ValidatePasswordAsync(string password);
    Task<string> GetClientIdAsync();
    Task<ClientInfo> GetClientInfoAsync();
    Task AddConnectionAsync(string connectionId);
    Task RemoveConnectionAsync(string connectionId);
    Task LeaveConnectionAsync(string connectionId);
    Task<bool> IsInitializedAsync();
    Task<string> GetOrCreateConnectionAsync();
    Task DeactivateAsync();
}

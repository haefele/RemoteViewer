using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Server.Grains.Interfaces;
using RemoteViewer.Server.Grains.State;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared;
using System.Text;

using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Grains;

public sealed class ClientGrain(
    ILogger<ClientGrain> logger,
    IHubContext<ConnectionHub, IConnectionHubClient> hubContext,
    IGrainFactory grainFactory) : Grain, IClientGrain
{
    private readonly ClientGrainState _state = new();

    private string SignalrConnectionId => this.GetPrimaryKeyString();

    public async Task<string> InitializeAsync(string? displayName)
    {
        if (this._state.ClientId is not null)
        {
            logger.LogWarning("ClientGrain {SignalrId} already initialized", this.SignalrConnectionId);
            return this._state.ClientId;
        }

        this._state.ClientId = Guid.NewGuid().ToString();
        this._state.DisplayName = displayName ?? string.Empty;

        const string IdChars = "0123456789";
        var attempts = 0;

        while (true)
        {
            attempts++;
            var username = Random.Shared.GetString(IdChars, 10);
            var usernameGrain = grainFactory.GetGrain<IUsernameGrain>(username);

            if (await usernameGrain.TryClaimAsync(this.SignalrConnectionId))
            {
                this._state.Username = username;
                this._state.Password = GeneratePassword();
                break;
            }

            if (attempts > 10)
            {
                logger.LogWarning("Too many username collisions, attempt {Attempts}", attempts);
            }
        }

        logger.LogInformation(
            "Client initialized: ClientId={ClientId}, Username={Username}, SignalrId={SignalrId}",
            this._state.ClientId, this._state.Username, this.SignalrConnectionId);

        await hubContext.Clients.Client(this.SignalrConnectionId)
            .CredentialsAssigned(this._state.ClientId, FormatUsername(this._state.Username!), this._state.Password!);

        return this._state.ClientId;
    }

    public async Task<string> GenerateNewPasswordAsync()
    {
        if (this._state.Username is null)
            return string.Empty; // Not initialized, silently return

        this._state.Password = GeneratePassword();

        logger.LogInformation("Password regenerated for ClientId={ClientId}", this._state.ClientId);

        await hubContext.Clients.Client(this.SignalrConnectionId)
            .CredentialsAssigned(this._state.ClientId!, FormatUsername(this._state.Username), this._state.Password);

        return this._state.Password;
    }

    public async Task SetDisplayNameAsync(string displayName)
    {
        if (this._state.ClientId is null)
            return; // Not initialized, silently return

        this._state.DisplayName = displayName;

        logger.LogInformation("Display name changed for ClientId={ClientId} to {DisplayName}", this._state.ClientId, displayName);

        foreach (var connectionId in this._state.ActiveConnectionIds)
        {
            var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
            if (await connectionGrain.IsActiveAsync())
            {
                await this.NotifyConnectionChangedAsync(connectionGrain);
            }
        }
    }

    [ReadOnly]
    public Task<bool> ValidatePasswordAsync(string password)
    {
        if (this._state.Password is null)
            return Task.FromResult(false);

        return Task.FromResult(string.Equals(this._state.Password, password, StringComparison.OrdinalIgnoreCase));
    }

    [ReadOnly]
    public Task<string> GetClientIdAsync()
    {
        return Task.FromResult(this._state.ClientId ?? string.Empty);
    }

    [ReadOnly]
    public Task<ClientInfo> GetClientInfoAsync()
    {
        return Task.FromResult(new ClientInfo(this._state.ClientId ?? string.Empty, this._state.DisplayName));
    }

    public Task AddConnectionAsync(string connectionId)
    {
        if (!this._state.ActiveConnectionIds.Contains(connectionId))
        {
            this._state.ActiveConnectionIds.Add(connectionId);
        }
        return Task.CompletedTask;
    }

    public Task RemoveConnectionAsync(string connectionId)
    {
        this._state.ActiveConnectionIds.Remove(connectionId);
        return Task.CompletedTask;
    }

    public async Task LeaveConnectionAsync(string connectionId)
    {
        this._state.ActiveConnectionIds.Remove(connectionId);

        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.RemoveClientAsync(this.SignalrConnectionId);
    }

    [ReadOnly]
    public Task<bool> IsInitializedAsync()
    {
        return Task.FromResult(this._state.ClientId is not null);
    }

    public async Task<string> GetOrCreateConnectionAsync()
    {
        if (this._state.ClientId is null)
            throw new InvalidOperationException("Client not initialized");

        if (this._state.PresenterConnectionId is not null)
        {
            var existingConnection = grainFactory.GetGrain<IConnectionGrain>(this._state.PresenterConnectionId);
            if (await existingConnection.IsActiveAsync())
            {
                return this._state.PresenterConnectionId;
            }
        }

        // Create new connection - manage our own state first
        var connectionId = Guid.NewGuid().ToString();
        this._state.PresenterConnectionId = connectionId;
        this._state.ActiveConnectionIds.Add(connectionId);

        var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
        await connectionGrain.InitializeAsync(this.SignalrConnectionId);

        logger.LogInformation("Created connection: ConnectionId={ConnectionId}, Presenter={SignalrId}", connectionId, this.SignalrConnectionId);

        return connectionId;
    }

    public async Task DeactivateAsync()
    {
        if (this._state.ClientId is null)
        {
            this.DeactivateOnIdle();
            return; // Not initialized, just deactivate
        }

        logger.LogInformation("Client deactivating: ClientId={ClientId}, SignalrId={SignalrId}", this._state.ClientId, this.SignalrConnectionId);

        // Release our username
        if (this._state.Username is not null)
        {
            var usernameGrain = grainFactory.GetGrain<IUsernameGrain>(this._state.Username);
            await usernameGrain.ReleaseAsync(this.SignalrConnectionId);
        }

        // Remove from all connections we're a viewer in
        foreach (var connectionId in this._state.ActiveConnectionIds.ToList())
        {
            var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(connectionId);
            await connectionGrain.RemoveClientAsync(this.SignalrConnectionId);
        }

        // If we're a presenter, close our connection
        if (this._state.PresenterConnectionId is not null)
        {
            var connectionGrain = grainFactory.GetGrain<IConnectionGrain>(this._state.PresenterConnectionId);
            await connectionGrain.RemoveClientAsync(this.SignalrConnectionId);
            this._state.PresenterConnectionId = null;
        }

        logger.LogInformation("Client deactivated: ClientId={ClientId}, SignalrId={SignalrId}", this._state.ClientId, this.SignalrConnectionId);

        this.DeactivateOnIdle();
    }

    private static string GeneratePassword()
    {
        const string PasswordChars = "abcdefghijklmnopqrstuvwxyz0123456789";
        return Random.Shared.GetString(PasswordChars, 8);
    }

    private static string FormatUsername(string username)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < username.Length; i++)
        {
            if (i > 0 && (username.Length - i) % 3 == 0)
                sb.Append(' ');
            sb.Append(username[i]);
        }
        return sb.ToString();
    }

    private async Task NotifyConnectionChangedAsync(IConnectionGrain connectionGrain)
    {
        var presenterSignalrId = await connectionGrain.GetPresenterSignalrIdAsync();
        var viewerSignalrIds = await connectionGrain.GetViewerSignalrIdsAsync();
        var properties = await connectionGrain.GetPropertiesAsync();

        if (presenterSignalrId is null)
            return;

        // Avoid calling back to self - get info directly from state if we're the presenter
        ClientInfo presenterInfo;
        if (presenterSignalrId == this.SignalrConnectionId)
        {
            presenterInfo = new ClientInfo(this._state.ClientId ?? string.Empty, this._state.DisplayName);
        }
        else
        {
            var presenterGrain = grainFactory.GetGrain<IClientGrain>(presenterSignalrId);
            presenterInfo = await presenterGrain.GetClientInfoAsync();
        }

        var viewerInfos = new List<ClientInfo>();
        foreach (var viewerSignalrId in viewerSignalrIds)
        {
            // Avoid calling back to self - get info directly from state if we're a viewer
            if (viewerSignalrId == this.SignalrConnectionId)
            {
                viewerInfos.Add(new ClientInfo(this._state.ClientId ?? string.Empty, this._state.DisplayName));
            }
            else
            {
                var viewerGrain = grainFactory.GetGrain<IClientGrain>(viewerSignalrId);
                viewerInfos.Add(await viewerGrain.GetClientInfoAsync());
            }
        }

        var connectionInfo = new ConnectionInfo(
            connectionGrain.GetPrimaryKeyString(),
            presenterInfo,
            viewerInfos,
            properties);

        await hubContext.Clients.Client(presenterSignalrId).ConnectionChanged(connectionInfo);
        foreach (var viewerSignalrId in viewerSignalrIds)
        {
            await hubContext.Clients.Client(viewerSignalrId).ConnectionChanged(connectionInfo);
        }
    }
}

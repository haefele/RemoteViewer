using Microsoft;
using Microsoft.AspNetCore.SignalR;
using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Server.Hubs;
using RemoteViewer.Shared;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using ConnectionInfo = RemoteViewer.Shared.ConnectionInfo;

namespace RemoteViewer.Server.Grains;

public interface IClientGrain : IGrainWithStringKey
{
    Task Initialize(string? displayName);
    [ReadOnly]
    Task<bool> IsInitialized();
    Task Deactivate();

    Task GenerateNewPassword();
    Task SetDisplayName(string displayName);

    Task<IConnectionGrain?> ValidatePasswordAndStartPresenting(string password);
    Task ViewerJoinConnection(IConnectionGrain connectionGrain);
    Task LeaveConnection(IConnectionGrain connectionGrain);

    [AlwaysInterleave]
    Task<string> Internal_GetClientId();
    [AlwaysInterleave]
    Task<(string ClientId, string DisplayName)> Internal_GetClientInfo();
}

public sealed class ClientGrain(ILogger<ClientGrain> logger, IHubContext<ConnectionHub, IConnectionHubClient> hubContext)
    : Grain, IClientGrain
{
    private string? _clientId;
    private IUsernameGrain? _usernameGrain;
    private string? _password;

    private string _displayName = string.Empty;

    private IConnectionGrain? _presenterConnectionGrain;
    private readonly HashSet<IConnectionGrain> _viewerConnectionGrains = new();

    public async Task Initialize(string? displayName)
    {
        if (this._clientId is not null)
            throw new InvalidOperationException("Client already initialized");

        this._clientId = Guid.NewGuid().ToString();
        this._displayName = displayName ?? string.Empty;

        const string IdChars = "0123456789";
        var attempts = 0;

        while (true)
        {
            attempts++;
            var username = Random.Shared.GetString(IdChars, 10);
            var usernameGrain = this.GrainFactory.GetGrain<IUsernameGrain>(username);

            if (await usernameGrain.TryClaimAsync(this.GetPrimaryKeyString()))
            {
                this._usernameGrain = usernameGrain;
                this._password = GeneratePassword();
                break;
            }
        }

        await hubContext.Clients
            .Client(this.GetPrimaryKeyString())
            .CredentialsAssigned(this._clientId, FormatUsername(this._usernameGrain.GetPrimaryKeyString()), this._password);
    }
    public Task<bool> IsInitialized()
    {
        return Task.FromResult(this._clientId is not null);
    }
    public async Task Deactivate()
    {
        this.EnsureInitialized();

        if (this._presenterConnectionGrain is not null)
        {
            await this._presenterConnectionGrain.Internal_RemoveClient(this);
            this._presenterConnectionGrain = null;
        }
        foreach (var connection in this._viewerConnectionGrains)
        {
            await connection.Internal_RemoveClient(this);
        }

        await this._usernameGrain.ReleaseAsync(this.GetPrimaryKeyString());

        this.DeactivateOnIdle();
    }

    public async Task GenerateNewPassword()
    {
        this.EnsureInitialized();

        this._password = GeneratePassword();

        await hubContext.Clients.Client(this.GetPrimaryKeyString())
            .CredentialsAssigned(this._clientId, FormatUsername(this._usernameGrain.GetPrimaryKeyString()), this._password);
    }
    public async Task SetDisplayName(string displayName)
    {
        this.EnsureInitialized();

        this._displayName = displayName;

        if (this._presenterConnectionGrain is not null)
            await this._presenterConnectionGrain.Internal_DisplayNameChanged();

        foreach (var connection in this._viewerConnectionGrains)
            await connection.Internal_DisplayNameChanged();
    }

    public async Task<IConnectionGrain?> ValidatePasswordAndStartPresenting(string password)
    {
        this.EnsureInitialized();

        if (string.Equals(this._password, password, StringComparison.OrdinalIgnoreCase) == false)
        {
            logger.LogWarning("Invalid password attempt for ClientId={ClientId}", this._clientId);
            return null;
        }

        if (this._presenterConnectionGrain is not null)
            return this._presenterConnectionGrain;

        // Create new connection
        var connectionId = Guid.NewGuid().ToString();
        this._presenterConnectionGrain = this.GrainFactory.GetGrain<IConnectionGrain>(connectionId);
        await this._presenterConnectionGrain.Internal_InitializePresenter(this);

        logger.LogInformation("Created connection: ConnectionId={ConnectionId}, Presenter={SignalrId}", connectionId, this.GetPrimaryKeyString());

        return this._presenterConnectionGrain;
    }
    public async Task ViewerJoinConnection(IConnectionGrain connectionGrain)
    {
        if (connectionGrain == this._presenterConnectionGrain)
            return;

        this._viewerConnectionGrains.Add(connectionGrain);
        await connectionGrain.Internal_AddViewer(this);
    }
    public async Task LeaveConnection(IConnectionGrain connectionGrain)
    {
        if (this._presenterConnectionGrain?.GetGrainId() == connectionGrain.GetGrainId())
        {
            this._presenterConnectionGrain = null;
        }
        else
        {
            this._viewerConnectionGrains.RemoveWhere(f => f.GetGrainId() == connectionGrain.GetGrainId());
        }

        await connectionGrain.Internal_RemoveClient(this);
    }

    public Task<string> Internal_GetClientId()
    {
        this.EnsureInitialized();

        return Task.FromResult(this._clientId);
    }
    public Task<(string ClientId, string DisplayName)> Internal_GetClientInfo()
    {
        this.EnsureInitialized();

        return Task.FromResult((this._clientId, this._displayName));
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
    [MemberNotNull(nameof(_clientId), nameof(_usernameGrain), nameof(_password))]
    private void EnsureInitialized()
    {
        if (this._clientId is null || this._usernameGrain is null || this._password is null)
            throw new InvalidOperationException("Client not initialized");
    }

}

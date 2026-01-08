using Microsoft.AspNetCore.SignalR;
using Orleans.Concurrency;
using RemoteViewer.Server.Hubs;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace RemoteViewer.Server.Orleans.Grains;

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

public sealed partial class ClientGrain(ILogger<ClientGrain> logger, IHubContext<ConnectionHub, IConnectionHubClient> hubContext)
    : Grain, IClientGrain
{
    private string? _clientId;
    private IUsernameGrain? _usernameGrain;
    private string? _password;

    private string _displayName = string.Empty;

    private IConnectionGrain? _presenterConnectionGrain;
    private readonly List<IConnectionGrain> _viewerConnectionGrains = [];

    public async Task Initialize(string? displayName)
    {
        if (this._clientId is not null)
            throw new InvalidOperationException("Client already initialized");

        this._clientId = Guid.NewGuid().ToString();
        this._displayName = displayName ?? string.Empty;

        this.LogClientInitializing(this._clientId);

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

            this.LogUsernameCollision(attempts);
        }

        this.LogClientInitialized(this._clientId, this._usernameGrain.GetPrimaryKeyString());

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

        this.LogClientDeactivating(this._clientId);

        if (this._presenterConnectionGrain is not null)
        {
            await this._presenterConnectionGrain.Internal_RemoveClient(this.AsReference<IClientGrain>());
            this._presenterConnectionGrain = null;
        }
        foreach (var connection in this._viewerConnectionGrains)
        {
            await connection.Internal_RemoveClient(this.AsReference<IClientGrain>());
        }

        await this._usernameGrain.ReleaseAsync(this.GetPrimaryKeyString());

        this.DeactivateOnIdle();
    }

    public async Task GenerateNewPassword()
    {
        this.EnsureInitialized();

        this._password = GeneratePassword();

        this.LogPasswordRegenerated(this._clientId);

        await hubContext.Clients.Client(this.GetPrimaryKeyString())
            .CredentialsAssigned(this._clientId, FormatUsername(this._usernameGrain.GetPrimaryKeyString()), this._password);
    }
    public async Task SetDisplayName(string displayName)
    {
        this.EnsureInitialized();

        this._displayName = displayName;

        this.LogDisplayNameChanged(displayName, this._clientId);

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
            this.LogInvalidPasswordAttempt(this._clientId);
            return null;
        }

        if (this._presenterConnectionGrain is not null)
            return this._presenterConnectionGrain;

        // Create new connection
        var connectionId = Guid.NewGuid().ToString();
        this._presenterConnectionGrain = this.GrainFactory.GetGrain<IConnectionGrain>(connectionId);
        await this._presenterConnectionGrain.Internal_InitializePresenter(this.AsReference<IClientGrain>());

        this.LogConnectionCreated(this._clientId, connectionId);

        return this._presenterConnectionGrain;
    }
    public async Task ViewerJoinConnection(IConnectionGrain connectionGrain)
    {
        this.EnsureInitialized();

        if (connectionGrain.GetGrainId() == this._presenterConnectionGrain?.GetGrainId())
            return;

        if (this._viewerConnectionGrains.Any(g => g.GetGrainId() == connectionGrain.GetGrainId()))
            return;

        this._viewerConnectionGrains.Add(connectionGrain);
        await connectionGrain.Internal_AddViewer(this.AsReference<IClientGrain>());

        this.LogViewerJoinedConnection(this._clientId, connectionGrain.GetPrimaryKeyString());
    }
    public async Task LeaveConnection(IConnectionGrain connectionGrain)
    {
        this.EnsureInitialized();

        this.LogClientLeftConnection(this._clientId, connectionGrain.GetPrimaryKeyString());

        if (object.Equals(this._presenterConnectionGrain, connectionGrain))
        {
            this._presenterConnectionGrain = null;
        }
        else
        {
            this._viewerConnectionGrains.Remove(connectionGrain);
        }

        await connectionGrain.Internal_RemoveClient(this.AsReference<IClientGrain>());
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
        {
            throw new InvalidOperationException(
                $"ClientGrain not initialized: clientId={(this._clientId is null ? "null" : "set")}, " +
                $"usernameGrain={(this._usernameGrain is null ? "null" : "set")}, " +
                $"password={(this._password is null ? "null" : "set")}");
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing client {ClientId}")]
    private partial void LogClientInitializing(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Username collision on attempt {Attempt}, retrying")]
    private partial void LogUsernameCollision(int attempt);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ClientId} initialized with username {Username}")]
    private partial void LogClientInitialized(string clientId, string username);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ClientId} deactivating")]
    private partial void LogClientDeactivating(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Password regenerated for client {ClientId}")]
    private partial void LogPasswordRegenerated(string clientId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Display name changed to {DisplayName} for client {ClientId}")]
    private partial void LogDisplayNameChanged(string displayName, string clientId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid password attempt for client {ClientId}")]
    private partial void LogInvalidPasswordAttempt(string clientId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Client {ClientId} started presenting on connection {ConnectionId}")]
    private partial void LogConnectionCreated(string clientId, string connectionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ClientId} joined connection {ConnectionId} as viewer")]
    private partial void LogViewerJoinedConnection(string clientId, string connectionId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Client {ClientId} left connection {ConnectionId}")]
    private partial void LogClientLeftConnection(string clientId, string connectionId);
}

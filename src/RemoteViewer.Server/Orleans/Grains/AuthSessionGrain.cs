using System.Security.Cryptography;
using Orleans;
using Orleans.Concurrency;

namespace RemoteViewer.Server.Orleans.Grains;

public interface IAuthSessionGrain : IGrainWithStringKey
{
    Task<string> IssueNonceAsync(string clientGuid);
    Task<AuthSessionStatus> GetStatusAsync();
    Task<bool> TryCompleteAsync(string clientGuid, string signatureBase64);
    Task<string?> GetAuthenticatedClientGuidAsync();
}

public enum AuthSessionStatus
{
    Pending,
    Authenticated,
    Expired,
}

public sealed partial class AuthSessionGrain(
    IGrainFactory grainFactory,
    TimeProvider timeProvider)
    : Grain, IAuthSessionGrain
{
    private static readonly TimeSpan s_nonceTtl = TimeSpan.FromMinutes(2);
    private readonly TimeProvider _timeProvider = timeProvider;
    private string? _nonce;
    private DateTimeOffset? _expiresAt;
    private string? _authenticatedClientGuid;
    private string? _nonceClientGuid;

    public async Task<string> IssueNonceAsync(string clientGuid)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        this._nonce = nonce;
        this._nonceClientGuid = clientGuid;
        this._authenticatedClientGuid = null;
        this._expiresAt = this._timeProvider.GetUtcNow().Add(s_nonceTtl);
        return nonce;
    }

    public Task<AuthSessionStatus> GetStatusAsync()
    {
        if (this._authenticatedClientGuid is not null)
            return Task.FromResult(AuthSessionStatus.Authenticated);

        if (this.IsExpired())
            return Task.FromResult(AuthSessionStatus.Expired);

        return Task.FromResult(AuthSessionStatus.Pending);
    }

    public async Task<bool> TryCompleteAsync(string clientGuid, string signatureBase64)
    {
        if (this._authenticatedClientGuid is not null)
            return string.Equals(this._authenticatedClientGuid, clientGuid, StringComparison.Ordinal);

        if (this.IsExpired())
            return false;

        if (!string.Equals(this._nonceClientGuid, clientGuid, StringComparison.Ordinal))
            return false;

        if (this._nonce is null)
            return false;

        var identityGrain = grainFactory.GetGrain<IClientIdentityGrain>(clientGuid);
        var publicKey = await identityGrain.GetPublicKeyAsync();
        var keyFormat = await identityGrain.GetKeyFormatAsync();
        if (publicKey is null || keyFormat is null)
            return false;

        if (!RemoteViewer.Server.Services.ClientAuthCrypto.VerifyNonce(this._nonce, publicKey, keyFormat, signatureBase64))
            return false;

        this._authenticatedClientGuid = clientGuid;
        return true;
    }

    public Task<string?> GetAuthenticatedClientGuidAsync()
    {
        return Task.FromResult(this._authenticatedClientGuid);
    }

    private bool IsExpired()
    {
        var expiresAt = this._expiresAt;
        if (expiresAt is null)
            return true;

        return this._timeProvider.GetUtcNow() > expiresAt.Value;
    }
}

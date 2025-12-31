using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace RemoteViewer.Server.Services;

public interface IIpcTokenService
{
    string GenerateToken(string connectionId);
    string? ValidateAndConsumeToken(string token);
}

public class IpcTokenService : IIpcTokenService, IDisposable
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();
    private readonly ITimer _cleanupTimer;
    private static readonly TimeSpan s_tokenTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_cleanupInterval = TimeSpan.FromSeconds(10);

    public IpcTokenService(TimeProvider timeProvider)
    {
        this._timeProvider = timeProvider;
        this._cleanupTimer = timeProvider.CreateTimer(_ => this.CleanupExpiredTokens(), null, s_cleanupInterval, s_cleanupInterval);
    }

    public string GenerateToken(string connectionId)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);

        this._tokens[token] = new TokenInfo(connectionId, this._timeProvider.GetUtcNow());

        return token;
    }

    public string? ValidateAndConsumeToken(string token)
    {
        // Remove and consume atomically - if TryRemove fails, token was already used or doesn't exist
        if (this._tokens.TryRemove(token, out var tokenInfo) is false)
            return null;

        // Check if expired
        if (this._timeProvider.GetUtcNow() - tokenInfo.CreatedAt > s_tokenTtl)
            return null;

        return tokenInfo.ConnectionId;
    }

    private void CleanupExpiredTokens()
    {
        var now = this._timeProvider.GetUtcNow();
        foreach (var kvp in this._tokens)
        {
            if (now - kvp.Value.CreatedAt > s_tokenTtl)
            {
                this._tokens.TryRemove(kvp.Key, out _);
            }
        }
    }

    public void Dispose()
    {
        this._cleanupTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record TokenInfo(string ConnectionId, DateTimeOffset CreatedAt);
}

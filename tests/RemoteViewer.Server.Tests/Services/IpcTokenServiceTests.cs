using Microsoft.Extensions.Time.Testing;
using RemoteViewer.Server.Services;

namespace RemoteViewer.Server.Tests.Services;

public class IpcTokenServiceTests : IDisposable
{
    private readonly FakeTimeProvider _timeProvider;
    private readonly IpcTokenService _service;

    public IpcTokenServiceTests()
    {
        this._timeProvider = new FakeTimeProvider();
        this._service = new IpcTokenService(this._timeProvider);
    }

    public void Dispose()
    {
        this._service.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task GenerateTokenReturnsBase64Token()
    {
        var token = this._service.GenerateToken("connection-123");

        await Assert.That(token).IsNotNull().And.IsNotEmpty();
        // Base64 of 32 bytes = 44 characters (with padding)
        await Assert.That(token).Length().IsEqualTo(44);
        // Should be valid base64
        var bytes = Convert.FromBase64String(token);
        await Assert.That(bytes).Count().IsEqualTo(32);
    }

    [Test]
    public async Task GenerateTokenReturnsUniqueTokensEachCall()
    {
        var token1 = this._service.GenerateToken("connection-1");
        var token2 = this._service.GenerateToken("connection-1");
        var token3 = this._service.GenerateToken("connection-2");

        await Assert.That(token1).IsNotEqualTo(token2);
        await Assert.That(token1).IsNotEqualTo(token3);
        await Assert.That(token2).IsNotEqualTo(token3);
    }

    [Test]
    public async Task ValidateAndConsumeTokenValidTokenReturnsConnectionId()
    {
        const string connectionId = "connection-123";
        var token = this._service.GenerateToken(connectionId);

        var result = this._service.ValidateAndConsumeToken(token);

        await Assert.That(result).IsEqualTo(connectionId);
    }

    [Test]
    public async Task ValidateAndConsumeTokenInvalidTokenReturnsNull()
    {
        var result = this._service.ValidateAndConsumeToken("invalid-token");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndConsumeTokenConsumedTokenReturnsNullOnSecondAttempt()
    {
        var token = this._service.GenerateToken("connection-123");

        var firstResult = this._service.ValidateAndConsumeToken(token);
        var secondResult = this._service.ValidateAndConsumeToken(token);

        await Assert.That(firstResult).IsEqualTo("connection-123");
        await Assert.That(secondResult).IsNull();
    }

    [Test]
    public async Task ValidateAndConsumeTokenExpiredTokenReturnsNull()
    {
        var token = this._service.GenerateToken("connection-123");

        // Advance time past the 30-second TTL
        this._timeProvider.Advance(TimeSpan.FromSeconds(31));

        var result = this._service.ValidateAndConsumeToken(token);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ValidateAndConsumeTokenJustBeforeExpirationSucceeds()
    {
        var token = this._service.GenerateToken("connection-123");

        // Advance time to just before expiration (29 seconds)
        this._timeProvider.Advance(TimeSpan.FromSeconds(29));

        var result = this._service.ValidateAndConsumeToken(token);

        await Assert.That(result).IsEqualTo("connection-123");
    }

    [Test]
    public async Task ConcurrentTokenOperationsAreThreadSafe()
    {
        const int tokenCount = 100;
        var tokens = new string[tokenCount];
        var connectionIds = new string[tokenCount];

        // Generate tokens
        for (var i = 0; i < tokenCount; i++)
        {
            connectionIds[i] = $"connection-{i}";
            tokens[i] = this._service.GenerateToken(connectionIds[i]);
        }

        // Validate concurrently
        var results = new string?[tokenCount];
        var tasks = new Task[tokenCount];
        for (var i = 0; i < tokenCount; i++)
        {
            var index = i;
            tasks[i] = Task.Run(() =>
            {
                results[index] = this._service.ValidateAndConsumeToken(tokens[index]);
            });
        }

        await Task.WhenAll(tasks);

        // All should have succeeded
        for (var i = 0; i < tokenCount; i++)
        {
            await Assert.That(results[i]).IsEqualTo(connectionIds[i]);
        }

        // Second validation should fail for all
        for (var i = 0; i < tokenCount; i++)
        {
            var secondResult = this._service.ValidateAndConsumeToken(tokens[i]);
            await Assert.That(secondResult).IsNull();
        }
    }

    [Test]
    public async Task MultipleTokensForSameConnectionAllValid()
    {
        const string connectionId = "connection-123";

        var token1 = this._service.GenerateToken(connectionId);
        var token2 = this._service.GenerateToken(connectionId);

        var result1 = this._service.ValidateAndConsumeToken(token1);
        var result2 = this._service.ValidateAndConsumeToken(token2);

        await Assert.That(result1).IsEqualTo(connectionId);
        await Assert.That(result2).IsEqualTo(connectionId);
    }
}

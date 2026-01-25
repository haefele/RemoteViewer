using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.Auth;

public sealed class ClientIdentityService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<ClientIdentityService> _logger;
    private readonly SemaphoreSlim _identityLock = new(1, 1);
    private readonly string _identityPath;
    private ClientIdentity? _cachedIdentity;

    public ClientIdentityService(ILogger<ClientIdentityService> logger)
    {
        this._logger = logger;
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        this._identityPath = Path.Combine(root, "Remote Viewer Client", "identity.json");
    }

    public async Task<ClientIdentity> GetIdentityAsync()
    {
        if (this._cachedIdentity is not null)
            return this._cachedIdentity;

        await this._identityLock.WaitAsync();
        try
        {
            if (this._cachedIdentity is not null)
                return this._cachedIdentity;

            var identity = await this.TryLoadIdentityAsync() ?? await this.CreateIdentityAsync();
            this._cachedIdentity = identity;
            return identity;
        }
        finally
        {
            this._identityLock.Release();
        }
    }

    private async Task<ClientIdentity?> TryLoadIdentityAsync()
    {
        if (!File.Exists(this._identityPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(this._identityPath);
            var data = JsonSerializer.Deserialize<ClientIdentityFile>(json, s_jsonOptions);
            if (data is null || string.IsNullOrWhiteSpace(data.ClientGuid) || string.IsNullOrWhiteSpace(data.PrivateKeyBase64))
                return null;

            var privateKey = Convert.FromBase64String(data.PrivateKeyBase64);
            if (data.IsProtected && OperatingSystem.IsWindows())
            {
                privateKey = ProtectedData.Unprotect(privateKey, null, DataProtectionScope.CurrentUser);
            }

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(privateKey, out _);
            var publicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());

            return new ClientIdentity(data.ClientGuid, privateKey, publicKeyBase64);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to load client identity, regenerating");
            return null;
        }
    }

    private async Task<ClientIdentity> CreateIdentityAsync()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        var publicKeyBase64 = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
        var clientGuid = Guid.NewGuid().ToString();

        var (storedKey, isProtected) = this.ProtectIfPossible(privateKey);
        var data = new ClientIdentityFile(clientGuid, Convert.ToBase64String(storedKey), isProtected);

        var directory = Path.GetDirectoryName(this._identityPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(data, s_jsonOptions);
        await File.WriteAllTextAsync(this._identityPath, json);

        this._logger.LogInformation("Created new client identity: {ClientGuid}", clientGuid);

        return new ClientIdentity(clientGuid, privateKey, publicKeyBase64);
    }

    private (byte[] Data, bool IsProtected) ProtectIfPossible(byte[] privateKey)
    {
        if (!OperatingSystem.IsWindows())
            return (privateKey, false);

        try
        {
            var protectedKey = ProtectedData.Protect(privateKey, null, DataProtectionScope.CurrentUser);
            return (protectedKey, true);
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to protect client identity key; storing unprotected");
            return (privateKey, false);
        }
    }

    public ValueTask DisposeAsync()
    {
        this._identityLock.Dispose();
        return ValueTask.CompletedTask;
    }

    public sealed record ClientIdentity(string ClientGuid, byte[] PrivateKey, string PublicKeyBase64)
    {
        public string KeyFormat => ClientAuthKeyFormats.EcdsaP256;

        public string SignNonce(string nonceBase64)
        {
            var nonce = Convert.FromBase64String(nonceBase64);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(this.PrivateKey, out _);
            var signature = ecdsa.SignData(nonce, HashAlgorithmName.SHA256);
            return Convert.ToBase64String(signature);
        }
    }

    private sealed record ClientIdentityFile(string ClientGuid, string PrivateKeyBase64, bool IsProtected);
}

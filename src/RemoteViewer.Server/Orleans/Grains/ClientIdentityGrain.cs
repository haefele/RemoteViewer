using System.Security.Cryptography;
using Orleans;
using Orleans.Concurrency;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Orleans.Grains;

public interface IClientIdentityGrain : IGrainWithStringKey
{
    Task<bool> RegisterAsync(string publicKeyBase64, string keyFormat);
    [ReadOnly]
    Task<bool> IsRegisteredAsync();
    [ReadOnly]
    Task<string?> GetPublicKeyAsync();
    [ReadOnly]
    Task<string?> GetKeyFormatAsync();
}

[GenerateSerializer]
public sealed class ClientIdentityState
{
    [Id(0)] public string? PublicKeyBase64 { get; set; }
    [Id(1)] public string? KeyFormat { get; set; }
}

public sealed partial class ClientIdentityGrain([PersistentState("identity", "Default")] IPersistentState<ClientIdentityState> state)
    : Grain, IClientIdentityGrain
{
    public async Task<bool> RegisterAsync(string publicKeyBase64, string keyFormat)
    {
        if (string.IsNullOrWhiteSpace(publicKeyBase64))
            return false;

        if (state.State.PublicKeyBase64 is not null)
        {
            return string.Equals(state.State.PublicKeyBase64, publicKeyBase64, StringComparison.Ordinal) &&
                   string.Equals(state.State.KeyFormat, keyFormat, StringComparison.Ordinal);
        }

        if (IsValidPublicKey(publicKeyBase64, keyFormat) is false)
            return false;

        state.State.PublicKeyBase64 = publicKeyBase64;
        state.State.KeyFormat = keyFormat;
        await state.WriteStateAsync();
        return true;
    }

    public Task<bool> IsRegisteredAsync()
    {
        return Task.FromResult(state.State.PublicKeyBase64 is not null);
    }

    public Task<string?> GetPublicKeyAsync()
    {
        return Task.FromResult(state.State.PublicKeyBase64);
    }

    public Task<string?> GetKeyFormatAsync()
    {
        return Task.FromResult(state.State.KeyFormat);
    }

    private static bool IsValidPublicKey(string publicKeyBase64, string keyFormat)
    {
        if (!string.Equals(keyFormat, RemoteViewer.Shared.ClientAuthKeyFormats.EcdsaP256, StringComparison.Ordinal))
            return false;

        try
        {
            using var ecdsa = ECDsa.Create();
            var bytes = Convert.FromBase64String(publicKeyBase64);
            ecdsa.ImportSubjectPublicKeyInfo(bytes, out _);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

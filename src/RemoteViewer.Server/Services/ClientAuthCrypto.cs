using System.Security.Cryptography;

namespace RemoteViewer.Server.Services;

public static class ClientAuthCrypto
{
    public static bool VerifyNonce(string nonceBase64, string publicKeyBase64, string keyFormat, string signatureBase64)
    {
        if (!string.Equals(keyFormat, RemoteViewer.Shared.ClientAuthKeyFormats.EcdsaP256, StringComparison.Ordinal))
            return false;

        try
        {
            var nonceBytes = Convert.FromBase64String(nonceBase64);
            var signatureBytes = Convert.FromBase64String(signatureBase64);
            var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

            return ecdsa.VerifyData(nonceBytes, signatureBytes, HashAlgorithmName.SHA256);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

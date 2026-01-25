namespace RemoteViewer.Shared;

public sealed record ClientAuthChallenge(string? Nonce, long ExpiresAtUnixMs, string? Error);

public sealed record ClientAuthResponse(string ClientGuid, string Signature);

public sealed record ClientAuthResult(bool IsAuthenticated, string? Error);

public sealed record ClientRegistrationResponse(bool IsRegistered, string? Error);

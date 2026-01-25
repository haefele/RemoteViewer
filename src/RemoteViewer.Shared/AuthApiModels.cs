namespace RemoteViewer.Shared;

public sealed record ClientRegistrationRequest(string ClientGuid, string PublicKeyBase64, string KeyFormat);

public sealed record ClientAuthNonceRequest(string ClientGuid);

public sealed record ClientAuthRequest(string ClientGuid, string Signature, string DisplayName, string ClientVersion);

public sealed record ClientAuthTokenResponse(bool IsAuthenticated, string? AccessToken, long? ExpiresAtUnixMs, string? Error, string? ServerVersion);

public sealed record IpcTokenRequest(string ConnectionId);

public sealed record IpcTokenResponse(bool Success, string? Token, string? Error);

public sealed record IpcTokenValidateRequest(string Token);

public sealed record IpcTokenValidateResponse(bool Success, string? ConnectionId, string? Error);

using PolyType;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.WinServiceIpc;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface IWinServiceRpc
{
    Task<AuthenticateResult> Authenticate(string token, CancellationToken ct);

    Task<bool> SendSecureAttentionSequence(string connectionId, uint sessionId, CancellationToken ct);
}

public record AuthenticateResult(bool Success, string? Error);

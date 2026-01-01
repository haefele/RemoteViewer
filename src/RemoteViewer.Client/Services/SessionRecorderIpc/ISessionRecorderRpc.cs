using PolyType;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.SessionRecorderIpc;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface ISessionRecorderRpc
{
    // Authentication - validates IPC access for a connection
    Task<AuthenticateResult> Authenticate(string token, CancellationToken ct);

    // Shared memory handshake - returns token for a specific display's shared memory
    Task<string> GetSharedMemoryToken(string connectionId, string displayId, CancellationToken ct);

    // Display operations
    Task<DisplayDto[]> GetDisplays(string connectionId, CancellationToken ct);

    // Screenshot operations (shared memory for full frames, serialization for dirty regions)
    Task<SharedFrameResult> CaptureDisplayShared(string connectionId, string displayId, bool forceKeyframe, CancellationToken ct);

    // Input injection operations
    Task InjectMouseMove(string connectionId, string displayId, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseButton(string connectionId, string displayId, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseWheel(string connectionId, string displayId, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectKey(string connectionId, ushort keyCode, bool isDown, CancellationToken ct);
    Task ReleaseAllModifiers(string connectionId, CancellationToken ct);
}

public record AuthenticateResult(bool Success, string? Error);

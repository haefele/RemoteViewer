using PolyType;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.WindowsIpc;

[JsonRpcContract]
[GenerateShape(IncludeMethods = MethodShapeFlags.PublicInstance)]
public partial interface ISessionRecorderRpc
{
    // Shared memory handshake - returns token for a specific display's shared memory
    Task<string> GetSharedMemoryToken(string displayId, CancellationToken ct);

    // Display operations
    Task<DisplayDto[]> GetDisplays(CancellationToken ct);

    // Screenshot operations (shared memory for full frames, serialization for dirty regions)
    Task<SharedFrameResult> CaptureDisplayShared(string displayId, bool forceKeyframe, CancellationToken ct);

    // Input injection operations
    Task InjectMouseMove(string displayId, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseButton(string displayId, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseWheel(string displayId, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct);
    Task ReleaseAllModifiers(CancellationToken ct);

    // Secure Attention Sequence (Ctrl+Alt+Del)
    Task<bool> SendSecureAttentionSequence(CancellationToken ct);
}

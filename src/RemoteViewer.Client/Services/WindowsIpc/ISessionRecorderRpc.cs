namespace RemoteViewer.Client.Services.WindowsIpc;

public interface ISessionRecorderRpc
{
    // Display operations
    Task<DisplayDto[]> GetDisplays(CancellationToken ct);

    // Screenshot operations
    Task<GrabResultDto> CaptureDisplay(string displayName, bool forceKeyframe, CancellationToken ct);

    // Input injection operations
    Task InjectMouseMove(string displayName, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseButton(string displayName, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectMouseWheel(string displayName, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct);
    Task InjectKey(ushort keyCode, bool isDown, CancellationToken ct);
    Task ReleaseAllModifiers(CancellationToken ct);

    // Secure Attention Sequence (Ctrl+Alt+Del)
    Task<bool> SendSecureAttentionSequence(CancellationToken ct);
}

using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.HubClient;

internal interface IPresenterServiceImpl
{
    // Input handling
    void HandleMouseMove(string senderClientId, float x, float y);
    void HandleMouseButton(string senderClientId, float x, float y, MouseButton button, bool isDown);
    void HandleMouseWheel(string senderClientId, float x, float y, float deltaX, float deltaY);
    void HandleKey(string senderClientId, ushort keyCode, KeyModifiers modifiers, bool isDown);
    void HandleSecureAttentionSequence(string senderClientId);

    // Display selection
    string? GetViewerDisplayId(string viewerClientId);
    Task<string?> CycleViewerDisplayAsync(string viewerClientId, CancellationToken ct = default);
    Task<string?> SelectViewerDisplayAsync(string viewerClientId, string displayId, CancellationToken ct = default);
    Task<List<string>> GetViewerIdsWatchingDisplayAsync(string displayId, CancellationToken ct = default);
    Task<HashSet<string>> GetDisplaysWithViewers(CancellationToken ct = default);
}

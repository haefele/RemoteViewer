using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.InputInjection;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.WindowsSession;
using RemoteViewer.Server.SharedAPI;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.SessionRecorderIpc;

public class SessionRecorderRpcServer(
    IWin32SessionService win32SessionService,
    IDisplayService displayService,
    IScreenshotService screenshotService,
    IInputInjectionService inputInjectionService,
    ILogger<SessionRecorderRpcServer> logger) : ISessionRecorderRpc, IDisposable
{
    // Per-display shared memory buffers
    private readonly Dictionary<string, SharedFrameBuffer> _displayBuffers = [];
    private readonly object _buffersLock = new();

    // Authentication state
    private readonly HashSet<string> _authenticatedConnections = [];
    private readonly object _connectionsLock = new();

    public async Task<AuthenticateResult> Authenticate(string token, CancellationToken ct)
    {
        try
        {
            // Connect to SignalR with token header - server validates and returns connectionId
            var connection = new HubConnectionBuilder()
                .WithUrl($"{ConnectionHubClient.BaseUrl}/connection", options =>
                {
                    options.Headers.Add("X-Ipc-Token", token);
                })
                .Build();

            var validationResult = new TaskCompletionSource<string?>();

            connection.On<string?>("IpcTokenValidated", connectionId => validationResult.TrySetResult(connectionId));
            connection.Closed += _ =>
            {
                validationResult.TrySetResult(null);
                return Task.CompletedTask;
            };

            await connection.StartAsync(ct);

            // Wait for validation result (server will abort connection after sending)
            var validatedConnectionId = await validationResult.Task.WaitAsync(ct);

            await connection.DisposeAsync();

            if (validatedConnectionId is not null)
            {
                lock (this._connectionsLock)
                {
                    this._authenticatedConnections.Add(validatedConnectionId);
                }
                logger.ConnectionAuthenticated(validatedConnectionId);
                return new AuthenticateResult(true, null);
            }

            logger.AuthenticationRejected();
            return new AuthenticateResult(false, "Invalid or expired token");
        }
        catch (Exception ex)
        {
            logger.AuthenticationFailed(ex);
            return new AuthenticateResult(false, "Failed to validate token");
        }
    }

    private void ValidateConnectionId(string connectionId)
    {
        lock (this._connectionsLock)
        {
            if (!this._authenticatedConnections.Contains(connectionId))
                throw new InvalidOperationException("Connection ID not authenticated");
        }
    }

    public async Task<string> GetSharedMemoryToken(string connectionId, string displayId, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var display = await this.ResolveDisplayAsync(displayId, ct)
            ?? throw new ArgumentException($"Display not found: {displayId}");

        var buffer = this.EnsureDisplayBuffer(display);
        return buffer.Token;
    }

    public async Task<DisplayDto[]> GetDisplays(string connectionId, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var displays = await displayService.GetDisplays(null, ct);
        return displays.Select(d => d.ToIpcDto()).ToArray();
    }

    public async Task<SharedFrameResult> CaptureDisplayShared(string connectionId, string displayId, bool forceKeyframe, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null)
        {
            return new SharedFrameResult(GrabStatus.Failure, false, null, null);
        }

        if (forceKeyframe)
        {
            await screenshotService.ForceKeyframe(displayId, ct);
        }

        win32SessionService.SwitchToInputDesktop();

        using var result = await screenshotService.CaptureDisplay(display, null, ct);

        if (result.Status != GrabStatus.Success)
        {
            return new SharedFrameResult(result.Status, false, null, null);
        }

        var buffer = this.EnsureDisplayBuffer(display);
        var moveRegions = result.MoveRects.ToIpcDtos();

        // If we have a full frame, write it to shared memory at offset 0
        if (result.FullFramePixels is not null)
        {
            buffer.Write(result.FullFramePixels.Memory.Span);
            return new SharedFrameResult(GrabStatus.Success, true, null, moveRegions);
        }

        // Write dirty regions to shared memory sequentially
        var dirtyRegions = WriteDirtyRegionsToSharedMemory(buffer, result.DirtyRegions);
        return new SharedFrameResult(GrabStatus.Success, false, dirtyRegions, moveRegions);
    }

    private static SharedRegionInfo[]? WriteDirtyRegionsToSharedMemory(SharedFrameBuffer buffer, DirtyRegion[]? regions)
    {
        if (regions is null || regions.Length == 0)
            return null;

        var infos = new SharedRegionInfo[regions.Length];
        var offset = 0;

        for (var i = 0; i < regions.Length; i++)
        {
            var region = regions[i];
            var pixels = region.Pixels.Memory.Span;

            buffer.WriteAt(offset, pixels);

            infos[i] = new SharedRegionInfo(
                region.X,
                region.Y,
                region.Width,
                region.Height,
                offset);

            offset += pixels.Length;
        }

        return infos;
    }

    private SharedFrameBuffer EnsureDisplayBuffer(DisplayInfo display)
    {
        lock (this._buffersLock)
        {
            if (this._displayBuffers.TryGetValue(display.Id, out var existing))
            {
                // Check if resolution changed - recreate buffer if needed
                if (existing.Width == display.Width && existing.Height == display.Height)
                    return existing;

                logger.SharedMemoryResolutionChanged(display.Id, existing.Width, existing.Height, display.Width, display.Height);
                existing.Dispose();
                this._displayBuffers.Remove(display.Id);
            }

            var buffer = SharedFrameBuffer.CreateServer(display.Width, display.Height);
            this._displayBuffers[display.Id] = buffer;

            logger.SharedMemoryCreated(display.Id, buffer.Name, display.Width, display.Height);

            return buffer;
        }
    }

    public async Task InjectMouseMove(string connectionId, string displayId, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseMove(display, normalizedX, normalizedY, null, ct);
    }

    public async Task InjectMouseButton(string connectionId, string displayId, int button, bool isDown, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseButton(display, (MouseButton)button, isDown, normalizedX, normalizedY, null, ct);
    }

    public async Task InjectMouseWheel(string connectionId, string displayId, float deltaX, float deltaY, float normalizedX, float normalizedY, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);

        var display = await this.ResolveDisplayAsync(displayId, ct);
        if (display is null) return;

        await inputInjectionService.InjectMouseWheel(display, deltaX, deltaY, normalizedX, normalizedY, null, ct);
    }

    public Task InjectKey(string connectionId, ushort keyCode, bool isDown, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);
        return inputInjectionService.InjectKey(keyCode, isDown, null, ct);
    }

    public Task ReleaseAllModifiers(string connectionId, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);
        return inputInjectionService.ReleaseAllModifiers(null, ct);
    }

    private async Task<DisplayInfo?> ResolveDisplayAsync(string displayId, CancellationToken ct)
    {
        var displays = await displayService.GetDisplays(null, ct);
        return displays.FirstOrDefault(d => d.Id == displayId);
    }

    public void Dispose()
    {
        lock (this._buffersLock)
        {
            foreach (var buffer in this._displayBuffers.Values)
                buffer.Dispose();

            this._displayBuffers.Clear();
        }
    }
}

using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.WindowsIpc;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.Screenshot;

public class IpcScreenGrabber(
    SessionRecorderRpcClient rpcClient,
    ILogger<IpcScreenGrabber> logger) : IScreenGrabber, IDisposable
{
    // Per-display shared memory buffers
    private readonly Dictionary<string, SharedFrameBuffer> _displayBuffers = [];
    private readonly SemaphoreSlim _buffersLock = new(1, 1);

    public bool IsAvailable => true;
    public int Priority => 200;

    public async Task<GrabResult> CaptureDisplay(DisplayInfo display, bool forceKeyframe, string? connectionId, CancellationToken ct)
    {
        if (connectionId is null || rpcClient.IsConnected is false || rpcClient.IsAuthenticatedFor(connectionId) is false)
            return new GrabResult { Status = GrabStatus.Failure };

        try
        {
            var sharedResult = await rpcClient.Proxy!.CaptureDisplayShared(connectionId, display.Id, forceKeyframe, ct);

            if (sharedResult.Status != GrabStatus.Success)
                return new GrabResult(sharedResult.Status, null, null, null);

            // Get shared memory buffer if we have pixel data to read
            SharedFrameBuffer? buffer = null;
            if (sharedResult.HasFullFrame || sharedResult.DirtyRegions is not null)
            {
                buffer = await this.EnsureDisplayBufferAsync(display, connectionId, ct);
            }

            // Read full frame from shared memory if present
            RefCountedMemoryOwner? fullFrame = null;
            if (sharedResult.HasFullFrame)
            {
                var frameSize = display.Width * display.Height * 4;
                fullFrame = RefCountedMemoryOwner.Create(frameSize);
                buffer!.Read(frameSize, fullFrame.Span);
            }

            // Read dirty regions from shared memory
            DirtyRegion[]? dirtyRegions = null;
            if (sharedResult.DirtyRegions is not null)
            {
                dirtyRegions = new DirtyRegion[sharedResult.DirtyRegions.Length];
                for (var i = 0; i < sharedResult.DirtyRegions.Length; i++)
                {
                    var r = sharedResult.DirtyRegions[i];
                    var pixels = RefCountedMemoryOwner.Create(r.ByteLength);
                    buffer!.ReadAt(r.Offset, r.ByteLength, pixels.Span);
                    dirtyRegions[i] = new DirtyRegion(r.X, r.Y, r.Width, r.Height, pixels);
                }
            }

            // Convert move regions from DTOs
            MoveRegion[]? moveRegions = null;
            if (sharedResult.MoveRegions is not null)
            {
                moveRegions = new MoveRegion[sharedResult.MoveRegions.Length];
                for (var i = 0; i < sharedResult.MoveRegions.Length; i++)
                {
                    var r = sharedResult.MoveRegions[i];
                    moveRegions[i] = new MoveRegion(r.SourceX, r.SourceY, r.DestinationX, r.DestinationY, r.Width, r.Height);
                }
            }

            return new GrabResult(GrabStatus.Success, fullFrame, dirtyRegions, moveRegions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.CaptureError(display.Id, ex);
            return new GrabResult { Status = GrabStatus.Failure };
        }
    }

    private async Task<SharedFrameBuffer> EnsureDisplayBufferAsync(DisplayInfo display, string connectionId, CancellationToken ct)
    {
        await this._buffersLock.WaitAsync(ct);
        try
        {
            if (this._displayBuffers.TryGetValue(display.Id, out var existing))
            {
                // Check if resolution changed - need to get new token from server
                if (existing.Width == display.Width && existing.Height == display.Height)
                    return existing;

                logger.SharedMemoryResolutionChanged(display.Id, existing.Width, existing.Height, display.Width, display.Height);
                existing.Dispose();
                this._displayBuffers.Remove(display.Id);
            }

            // Get the token from the server via secured RPC
            var token = await rpcClient.Proxy!.GetSharedMemoryToken(connectionId, display.Id, ct);
            var buffer = SharedFrameBuffer.OpenClient(token, display.Width, display.Height);

            this._displayBuffers[display.Id] = buffer;
            logger.SharedMemoryOpened(display.Id, buffer.Name);

            return buffer;
        }
        finally
        {
            this._buffersLock.Release();
        }
    }

    public void Dispose()
    {
        foreach (var buffer in this._displayBuffers.Values)
            buffer.Dispose();
        this._displayBuffers.Clear();
        this._buffersLock.Dispose();
    }
}

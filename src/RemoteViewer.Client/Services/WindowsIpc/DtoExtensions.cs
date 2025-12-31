using RemoteViewer.Client.Common;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.WindowsIpc;

public static class DtoExtensions
{
    // IPC DisplayDto -> DisplayInfo conversion
    public static DisplayInfo ToDisplayInfo(this DisplayDto dto) =>
        new(dto.Id, dto.FriendlyName, dto.IsPrimary, dto.Left, dto.Top, dto.Right, dto.Bottom);

    // DisplayInfo -> IPC DisplayDto conversion
    public static DisplayDto ToIpcDto(this DisplayInfo display) =>
        new(display.Id, display.FriendlyName, display.IsPrimary, display.Left, display.Top, display.Right, display.Bottom);

    // GrabResult -> GrabResultDto (server side, for sending over IPC)
    public static GrabResultDto ToDto(this GrabResult result)
    {
        DirtyRegionDto[]? dirtyRegions = null;
        if (result.DirtyRegions is not null)
        {
            dirtyRegions = new DirtyRegionDto[result.DirtyRegions.Length];
            for (var i = 0; i < result.DirtyRegions.Length; i++)
            {
                var r = result.DirtyRegions[i];
                dirtyRegions[i] = new DirtyRegionDto(r.X, r.Y, r.Width, r.Height, r.Pixels.Span.ToArray());
            }
        }

        MoveRegionDto[]? moveRegions = null;
        if (result.MoveRects is not null)
        {
            moveRegions = new MoveRegionDto[result.MoveRects.Length];
            for (var i = 0; i < result.MoveRects.Length; i++)
            {
                var r = result.MoveRects[i];
                moveRegions[i] = new MoveRegionDto(r.SourceX, r.SourceY, r.DestinationX, r.DestinationY, r.Width, r.Height);
            }
        }

        return new GrabResultDto(result.Status, result.FullFramePixels?.Span.ToArray(), dirtyRegions, moveRegions);
    }

    // GrabResultDto -> GrabResult (client side, after receiving over IPC)
    public static GrabResult FromDto(this GrabResultDto dto)
    {
        RefCountedMemoryOwner? fullFrame = null;
        if (dto.FullFramePixels is { } fullFramePixels)
        {
            fullFrame = RefCountedMemoryOwner.Create(fullFramePixels.Length);
            fullFramePixels.AsSpan().CopyTo(fullFrame.Span);
        }

        DirtyRegion[]? dirtyRegions = null;
        if (dto.DirtyRegions is not null)
        {
            dirtyRegions = new DirtyRegion[dto.DirtyRegions.Length];
            for (var i = 0; i < dto.DirtyRegions.Length; i++)
            {
                var r = dto.DirtyRegions[i];
                var pixels = RefCountedMemoryOwner.Create(r.Pixels.Length);
                r.Pixels.AsSpan().CopyTo(pixels.Span);
                dirtyRegions[i] = new DirtyRegion(r.X, r.Y, r.Width, r.Height, pixels);
            }
        }

        MoveRegion[]? moveRegions = null;
        if (dto.MoveRegions is not null)
        {
            moveRegions = new MoveRegion[dto.MoveRegions.Length];
            for (var i = 0; i < dto.MoveRegions.Length; i++)
            {
                var r = dto.MoveRegions[i];
                moveRegions[i] = new MoveRegion(r.SourceX, r.SourceY, r.DestinationX, r.DestinationY, r.Width, r.Height);
            }
        }

        return new GrabResult(dto.Status, fullFrame, dirtyRegions, moveRegions);
    }
}

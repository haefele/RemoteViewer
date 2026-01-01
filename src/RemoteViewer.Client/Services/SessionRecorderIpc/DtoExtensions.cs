using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.SessionRecorderIpc;

public static class DtoExtensions
{
    public static DisplayInfo ToDisplayInfo(this DisplayDto dto) =>
        new(dto.Id, dto.FriendlyName, dto.IsPrimary, dto.Left, dto.Top, dto.Right, dto.Bottom);

    public static DisplayDto ToIpcDto(this DisplayInfo display) =>
        new(display.Id, display.FriendlyName, display.IsPrimary, display.Left, display.Top, display.Right, display.Bottom);

    public static MoveRegionDto[]? ToIpcDtos(this MoveRegion[]? regions)
    {
        if (regions is null) return null;

        var dtos = new MoveRegionDto[regions.Length];
        for (var i = 0; i < regions.Length; i++)
        {
            var r = regions[i];
            dtos[i] = new MoveRegionDto(r.SourceX, r.SourceY, r.DestinationX, r.DestinationY, r.Width, r.Height);
        }
        return dtos;
    }
}

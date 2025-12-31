using System.Collections.Immutable;
using RemoteViewer.Server.SharedAPI;

namespace RemoteViewer.Client.Services.Displays;

public interface IDisplayService
{
    Task<ImmutableList<DisplayInfo>> GetDisplays(string? connectionId, CancellationToken ct);
}

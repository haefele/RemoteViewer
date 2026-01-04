using System.Collections.Immutable;
using RemoteViewer.Shared;

namespace RemoteViewer.Client.Services.Displays;

public interface IDisplayService
{
    Task<ImmutableList<DisplayInfo>> GetDisplays(string? connectionId, CancellationToken ct);
}

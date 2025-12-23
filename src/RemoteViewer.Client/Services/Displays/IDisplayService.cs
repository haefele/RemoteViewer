using System.Collections.Immutable;
using RemoteViewer.Client.Services.Screenshot;

namespace RemoteViewer.Client.Services.Displays;

public interface IDisplayService
{
    Task<ImmutableList<Display>> GetDisplays(CancellationToken ct);
}

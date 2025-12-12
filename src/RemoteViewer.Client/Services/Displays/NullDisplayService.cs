using System.Collections.Immutable;
using RemoteViewer.Client.Services.ScreenCapture;

namespace RemoteViewer.Client.Services.Displays;

public class NullDisplayService : IDisplayService
{
    public ImmutableList<Display> GetDisplays() => [];
}

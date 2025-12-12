using System.Collections.Immutable;
using RemoteViewer.Client.Services.ScreenCapture;

namespace RemoteViewer.Client.Services.Displays;

public interface IDisplayService
{
    ImmutableList<Display> GetDisplays();
}

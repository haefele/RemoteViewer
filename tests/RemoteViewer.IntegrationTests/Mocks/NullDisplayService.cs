using System.Collections.Immutable;
using RemoteViewer.Client.Services.Displays;
using RemoteViewer.Shared;

namespace RemoteViewer.IntegrationTests.Mocks;

public class NullDisplayService : IDisplayService
{
    private static readonly DisplayInfo FakeDisplay = new(
        Id: "DISPLAY1",
        FriendlyName: "Test Display",
        IsPrimary: true,
        Left: 0,
        Top: 0,
        Right: 1920,
        Bottom: 1080);

    public Task<ImmutableList<DisplayInfo>> GetDisplays(string? connectionId, CancellationToken ct)
        => Task.FromResult(ImmutableList.Create(FakeDisplay));
}

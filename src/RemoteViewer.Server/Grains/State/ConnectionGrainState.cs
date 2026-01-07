using RemoteViewer.Shared;

namespace RemoteViewer.Server.Grains.State;

[GenerateSerializer]
public sealed class ConnectionGrainState
{
    [Id(0)]
    public string? PresenterSignalrId { get; set; }

    [Id(1)]
    public HashSet<string> ViewerSignalrIds { get; set; } = [];

    [Id(2)]
    public ConnectionProperties Properties { get; set; } = new(false, [], []);

    [Id(3)]
    public bool IsClosed { get; set; }
}

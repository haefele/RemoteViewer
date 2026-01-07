namespace RemoteViewer.Server.Grains.State;

[GenerateSerializer]
public sealed class ClientGrainState
{
    [Id(0)]
    public string? ClientId { get; set; }

    [Id(1)]
    public string? Username { get; set; }

    [Id(2)]
    public string? Password { get; set; }

    [Id(3)]
    public string DisplayName { get; set; } = string.Empty;

    [Id(4)]
    public List<string> ActiveConnectionIds { get; set; } = [];

    [Id(5)]
    public string? PresenterConnectionId { get; set; }
}

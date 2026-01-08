using RemoteViewer.Shared;

namespace RemoteViewer.Server.Orleans.Surrogates;

[GenerateSerializer]
public struct DisplayInfoSurrogate
{
    [Id(0)] public string Id { get; set; }
    [Id(1)] public string FriendlyName { get; set; }
    [Id(2)] public bool IsPrimary { get; set; }
    [Id(3)] public int Left { get; set; }
    [Id(4)] public int Top { get; set; }
    [Id(5)] public int Right { get; set; }
    [Id(6)] public int Bottom { get; set; }
}

[RegisterConverter]
public sealed class DisplayInfoConverter : IConverter<DisplayInfo, DisplayInfoSurrogate>
{
    public DisplayInfo ConvertFromSurrogate(in DisplayInfoSurrogate surrogate) =>
        new(surrogate.Id, surrogate.FriendlyName, surrogate.IsPrimary,
            surrogate.Left, surrogate.Top, surrogate.Right, surrogate.Bottom);

    public DisplayInfoSurrogate ConvertToSurrogate(in DisplayInfo value) =>
        new()
        {
            Id = value.Id,
            FriendlyName = value.FriendlyName,
            IsPrimary = value.IsPrimary,
            Left = value.Left,
            Top = value.Top,
            Right = value.Right,
            Bottom = value.Bottom
        };
}

using Orleans;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Orleans.Surrogates;

[GenerateSerializer]
public struct ConnectionPropertiesSurrogate
{
    [Id(0)] public bool CanSendSecureAttentionSequence { get; set; }
    [Id(1)] public List<string> InputBlockedViewerIds { get; set; }
    [Id(2)] public List<DisplayInfo> AvailableDisplays { get; set; }
}

[RegisterConverter]
public sealed class ConnectionPropertiesConverter : IConverter<ConnectionProperties, ConnectionPropertiesSurrogate>
{
    public ConnectionProperties ConvertFromSurrogate(in ConnectionPropertiesSurrogate surrogate) =>
        new(surrogate.CanSendSecureAttentionSequence, surrogate.InputBlockedViewerIds, surrogate.AvailableDisplays);

    public ConnectionPropertiesSurrogate ConvertToSurrogate(in ConnectionProperties value) =>
        new()
        {
            CanSendSecureAttentionSequence = value.CanSendSecureAttentionSequence,
            InputBlockedViewerIds = value.InputBlockedViewerIds,
            AvailableDisplays = value.AvailableDisplays
        };
}

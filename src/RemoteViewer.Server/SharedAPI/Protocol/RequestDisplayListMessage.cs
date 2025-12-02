using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Request from viewer to get the current display list from the presenter.
/// Presenter responds with DisplayListMessage to the requesting viewer.
/// </summary>
[GenerateShape]
public sealed partial record RequestDisplayListMessage;

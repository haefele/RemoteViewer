using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Request from viewer to select a specific display to watch.
/// </summary>
/// <param name="DisplayId">Display ID to switch to</param>
[GenerateShape]
public sealed partial record DisplaySelectMessage(string DisplayId);

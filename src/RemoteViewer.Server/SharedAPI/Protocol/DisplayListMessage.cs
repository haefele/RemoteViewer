using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// List of available displays on the presenter's system.
/// Sent when a viewer connects and when display configuration changes.
/// </summary>
/// <param name="Displays">Array of available displays</param>
[GenerateShape]
public sealed partial record DisplayListMessage(DisplayInfo[] Displays);

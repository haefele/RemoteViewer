using PolyType;

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Mouse move event with normalized coordinates.
/// </summary>
/// <param name="X">X position as fraction of display width [0.0, 1.0]</param>
/// <param name="Y">Y position as fraction of display height [0.0, 1.0]</param>
/// <remarks>
/// Using normalized coordinates decouples viewer resolution from presenter resolution.
/// Presenter converts to absolute coordinates based on the target display bounds.
/// </remarks>
[GenerateShape]
public sealed partial record MouseMoveMessage(float X, float Y);

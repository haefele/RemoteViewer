namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Frame encoding codec identifier.
/// </summary>
public enum FrameCodec : byte
{
    /// <summary>JPEG encoding - good compression, some quality loss</summary>
    Jpeg = 0,

    /// <summary>PNG encoding - lossless, larger files</summary>
    Png = 1,

    // Reserved for future video codecs
    // Hevc = 10,
    // Av1 = 11,
    // Vp9 = 12,
}

/// <summary>
/// Type of frame being transmitted.
/// </summary>
public enum FrameType : byte
{
    /// <summary>Complete frame - use as base for delta reconstruction</summary>
    Keyframe = 0,

    /// <summary>Delta frame - contains only changed regions</summary>
    DeltaFrame = 1
}

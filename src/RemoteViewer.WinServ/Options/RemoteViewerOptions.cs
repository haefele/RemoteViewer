namespace RemoteViewer.WinServ.Options;

public class RemoteViewerOptions
{
    public RemoteViewerMode Mode { get; set; } = RemoteViewerMode.Undefined;
    public uint? SessionId { get; set; }
}

public enum RemoteViewerMode
{
    Undefined,
    WindowsService,
    SessionRecorder,
}

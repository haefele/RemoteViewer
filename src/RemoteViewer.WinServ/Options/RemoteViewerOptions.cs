namespace RemoteViewer.WinServ.Options;

public class RemoteViewerOptions
{
    public RemoteViewerMode Mode { get; set; } = RemoteViewerMode.WindowsService;
    public uint? SessionId { get; set; }
}

public enum RemoteViewerMode
{
    WindowsService,
    SessionRecorder,
}

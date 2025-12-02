namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Protocol message type identifiers used with SendMessage.
/// </summary>
public static class MessageTypes
{
    public static class Display
    {
        /// <summary>List of available displays (Presenter → Viewers)</summary>
        public const string List = "display.list";

        /// <summary>Request to watch a specific display (Viewer → Presenter)</summary>
        public const string Select = "display.select";

        /// <summary>Request display list (Viewer → Presenter)</summary>
        public const string RequestList = "display.list.request";
    }

    public static class Screen
    {
        /// <summary>Encoded screen frame data (Presenter → Specific Viewers)</summary>
        public const string Frame = "screen.frame";
    }

    public static class Input
    {
        /// <summary>Key pressed (Viewer → Presenter)</summary>
        public const string KeyDown = "input.key.down";

        /// <summary>Key released (Viewer → Presenter)</summary>
        public const string KeyUp = "input.key.up";

        /// <summary>Mouse position update (Viewer → Presenter)</summary>
        public const string MouseMove = "input.mouse.move";

        /// <summary>Mouse button pressed (Viewer → Presenter)</summary>
        public const string MouseDown = "input.mouse.down";

        /// <summary>Mouse button released (Viewer → Presenter)</summary>
        public const string MouseUp = "input.mouse.up";

        /// <summary>Mouse wheel scroll (Viewer → Presenter)</summary>
        public const string MouseWheel = "input.mouse.wheel";
    }
}

namespace RemoteViewer.Server.SharedAPI.Protocol;

/// <summary>
/// Protocol message type identifiers used with SendMessage.
/// </summary>
public static class MessageTypes
{
    public static class Display
    {
        /// <summary>Request to switch to the next display (Viewer → Presenter)</summary>
        public const string Switch = "display.switch";

        /// <summary>Request to switch to a specific display (Viewer → Presenter)</summary>
        public const string Select = "display.select";
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

        /// <summary>Secure Attention Sequence / Ctrl+Alt+Del (Viewer → Presenter)</summary>
        public const string SecureAttentionSequence = "input.sas";
    }

    public static class FileTransfer
    {
        /// <summary>Request to send a file (Bidirectional)</summary>
        public const string SendRequest = "file.send.request";

        /// <summary>Response to send request (Bidirectional)</summary>
        public const string SendResponse = "file.send.response";

        /// <summary>File chunk data (Bidirectional)</summary>
        public const string Chunk = "file.chunk";

        /// <summary>Transfer complete notification (Bidirectional)</summary>
        public const string Complete = "file.complete";

        /// <summary>Cancel transfer (Bidirectional)</summary>
        public const string Cancel = "file.cancel";

        /// <summary>Transfer error (Bidirectional)</summary>
        public const string Error = "file.error";
    }
}

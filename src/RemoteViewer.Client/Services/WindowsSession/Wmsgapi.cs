using System.Runtime.InteropServices;

namespace RemoteViewer.Client.Services.WindowsSession;

internal static partial class Wmsgapi
{
    private const int WM_SAS = 0x208;

    [LibraryImport("wmsgapi.dll", SetLastError = true)]
    private static partial int WmsgSendMessage(int sessionId, int msg, int wParam, nint lParam);

    public static int SendSasToSession(int sessionId)
    {
        // WmsgSendMessage requires a valid pointer for lParam (even though it's not used for SAS)
        var lParam = Marshal.AllocHGlobal(1);
        try
        {
            Marshal.WriteByte(lParam, 0);
            return WmsgSendMessage(sessionId, WM_SAS, 0, lParam);
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }
    }
}

using System;
using System.Drawing;

namespace RemoteViewer.DesktopDupTest;

public class ScreenshotService
{
    public virtual async Task<Bitmap> CaptureScreenshotAsync(CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var bitmap = new Bitmap(1920, 1080);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            }

            return bitmap;
        }, cancellationToken);
    }
}

#if WINDOWS
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace RemoteViewer.Client.Services.Windows;

public sealed class BitBltScreenGrabber(ILogger<BitBltScreenGrabber> logger)
{
    public unsafe CaptureResult CaptureDisplay(Display display, SKBitmap targetBuffer)
    {
        if (targetBuffer.Width != display.Bounds.Width || targetBuffer.Height != display.Bounds.Height)
            throw new ArgumentException($"Target buffer dimensions ({targetBuffer.Width}x{targetBuffer.Height}) do not match display dimensions ({display.Bounds.Width}x{display.Bounds.Height})", nameof(targetBuffer));

        var sourceDC = HDC.Null;
        var memoryDC = HDC.Null;
        DeleteObjectSafeHandle? hBitmapHandle = null;
        HGDIOBJ hOldBitmap = default;

        try
        {
            var width = display.Bounds.Width;
            var height = display.Bounds.Height;

            if (width <= 0 || height <= 0)
            {
                logger.LogWarning("Invalid display dimensions: {Width}x{Height}", width, height);
                return CaptureResult.Failure;
            }

            sourceDC = PInvoke.GetDC(HWND.Null);
            if (sourceDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to get source DC: {ErrorCode}", errorCode);
                return CaptureResult.Failure;
            }

            memoryDC = PInvoke.CreateCompatibleDC(sourceDC);
            if (memoryDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to create compatible DC: {ErrorCode}", errorCode);
                return CaptureResult.Failure;
            }

            var bitmapInfo = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                }
            };

            hBitmapHandle = PInvoke.CreateDIBSection(
                memoryDC,
                &bitmapInfo,
                DIB_USAGE.DIB_RGB_COLORS,
                out var pBits,
                null,
                0);

            if (hBitmapHandle.IsInvalid || pBits == null)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to create DIB section: {ErrorCode}", errorCode);
                return CaptureResult.Failure;
            }

            hOldBitmap = PInvoke.SelectObject(memoryDC, new HGDIOBJ(hBitmapHandle.DangerousGetHandle()));

            if (PInvoke.BitBlt(
                memoryDC,
                0,
                0,
                width,
                height,
                sourceDC,
                display.Bounds.Left,
                display.Bounds.Top,
                ROP_CODE.SRCCOPY) == false)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("BitBlt failed: {ErrorCode}", errorCode);
                return CaptureResult.Failure;
            }

            var stride = width * 4;
            var bufferSize = stride * height;

            Buffer.MemoryCopy(pBits, (void*)targetBuffer.GetPixels(), bufferSize, bufferSize);

            return CaptureResult.Ok(targetBuffer, []);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while capturing display {DisplayName}", display.Name);
            return CaptureResult.Failure;
        }
        finally
        {
            if (!hOldBitmap.IsNull)
            {
                PInvoke.SelectObject(memoryDC, hOldBitmap);
            }

            hBitmapHandle?.Dispose();

            if (!memoryDC.IsNull)
            {
                PInvoke.DeleteDC(memoryDC);
            }

            if (!sourceDC.IsNull)
            {
                var releaseResult = PInvoke.ReleaseDC(HWND.Null, sourceDC);
                if (releaseResult == 0)
                {
                    logger.LogWarning("Failed to release DC");
                }
            }
        }
    }
}
#endif

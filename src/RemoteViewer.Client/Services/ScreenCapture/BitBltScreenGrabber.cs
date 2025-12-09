#if WINDOWS
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace RemoteViewer.Client.Services.ScreenCapture;

public sealed class BitBltScreenGrabber(ILogger<BitBltScreenGrabber> logger)
{
    public unsafe GrabResult CaptureDisplay(Display display, Span<byte> targetBuffer)
    {
        var width = display.Bounds.Width;
        var height = display.Bounds.Height;
        var expectedSize = width * height * 4;

        if (targetBuffer.Length != expectedSize)
            throw new ArgumentException($"Target buffer size ({targetBuffer.Length}) is smaller than required ({expectedSize})", nameof(targetBuffer));

        var sourceDC = HDC.Null;
        var memoryDC = HDC.Null;
        DeleteObjectSafeHandle? hBitmapHandle = null;
        HGDIOBJ hOldBitmap = default;

        try
        {
            if (width <= 0 || height <= 0)
            {
                logger.LogWarning("Invalid display dimensions: {Width}x{Height}", width, height);
                return new GrabResult(GrabStatus.Failure, null);
            }

            sourceDC = PInvoke.GetDC(HWND.Null);
            if (sourceDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to get source DC: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null);
            }

            memoryDC = PInvoke.CreateCompatibleDC(sourceDC);
            if (memoryDC.IsNull)
            {
                var errorCode = Marshal.GetLastWin32Error();
                logger.LogError("Failed to create compatible DC: {ErrorCode}", errorCode);
                return new GrabResult(GrabStatus.Failure, null);
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
                return new GrabResult(GrabStatus.Failure, null);
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
                return new GrabResult(GrabStatus.Failure, null);
            }

            var bufferSize = expectedSize;

            fixed (byte* destPtr = targetBuffer)
            {
                Buffer.MemoryCopy(pBits, destPtr, bufferSize, bufferSize);
            }

            return new GrabResult(GrabStatus.Success, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while capturing display {DisplayName}", display.Name);
            return new GrabResult(GrabStatus.Failure, null);
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

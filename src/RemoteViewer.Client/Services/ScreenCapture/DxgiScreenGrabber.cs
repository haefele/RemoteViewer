#if WINDOWS
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace RemoteViewer.Client.Services.ScreenCapture;

public class DxgiScreenGrabber(ILogger<DxgiScreenGrabber> logger)
{
    private readonly Dictionary<string, DxOutput> _outputs = new();
    private readonly object _lock = new();

    public unsafe CaptureResult CaptureDisplay(Display display, SKBitmap targetBuffer)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return CaptureResult.Failure;

        if (targetBuffer.Width != display.Bounds.Width || targetBuffer.Height != display.Bounds.Height)
            throw new ArgumentException($"Target buffer dimensions ({targetBuffer.Width}x{targetBuffer.Height}) do not match display dimensions ({display.Bounds.Width}x{display.Bounds.Height})", nameof(targetBuffer));

        try
        {
            var dxOutput = this.GetOrCreateOutput(display.Name);
            if (dxOutput is null)
            {
                logger.LogDebug("Failed to get DXGI output for display {DisplayName}, falling back to BitBlt", display.Name);
                return CaptureResult.Failure;
            }

            var outputDuplication = dxOutput.OutputDuplication;
            var deviceContext = dxOutput.DeviceContext;

            try
            {
                outputDuplication.ReleaseFrame();
            }
            catch
            {
            }

            outputDuplication.AcquireNextFrame(0, out var frameInfo, out var screenResource);

            if (frameInfo.AccumulatedFrames == 0)
            {
                if (screenResource is not null)
                {
                    Marshal.FinalReleaseComObject(screenResource);
                }
                logger.LogDebug("No accumulated frames");
                return CaptureResult.NoChanges;
            }

            try
            {
                var width = display.Bounds.Width;
                var height = display.Bounds.Height;

                var stagingTexture = dxOutput.GetOrCreateStagingTexture(width, height);

                var sourceTexture = (ID3D11Texture2D)screenResource;
                deviceContext.CopyResource(stagingTexture, sourceTexture);

                var dirtyRects = this.GetDirtyRects(outputDuplication);

                D3D11_MAPPED_SUBRESOURCE mappedResource;
                deviceContext.Map(stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mappedResource);

                try
                {
                    var destPtr = (byte*)targetBuffer.GetPixels();
                    var sourcePtr = (byte*)mappedResource.pData;

                    if (width * 4 == mappedResource.RowPitch)
                    {
                        Unsafe.CopyBlock(destPtr, sourcePtr, (uint)(height * mappedResource.RowPitch));
                    }
                    else
                    {
                        for (var y = 0; y < height; y++)
                        {
                            var destRow = destPtr + y * width * 4;
                            var sourceRow = sourcePtr + y * mappedResource.RowPitch;
                            Unsafe.CopyBlock(destRow, sourceRow, (uint)(width * 4));
                        }
                    }

                    return CaptureResult.Ok(targetBuffer, dirtyRects);
                }
                finally
                {
                    deviceContext.Unmap(stagingTexture, 0);
                }
            }
            finally
            {
                if (screenResource is not null)
                {
                    Marshal.FinalReleaseComObject(screenResource);
                }
            }
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x887A0027)
        {
            return CaptureResult.NoChanges;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x887A0026)
        {
            logger.LogWarning("DXGI_ERROR_ACCESS_LOST - recreating output duplication");
            this.SetOutputFaulted(display.Name);

            return CaptureResult.Failure;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while capturing display {DisplayName} with DXGI", display.Name);
            this.SetOutputFaulted(display.Name);

            return CaptureResult.Failure;
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe Rectangle[] GetDirtyRects(IDXGIOutputDuplication outputDuplication)
    {
        try
        {
            var rectSize = (uint)sizeof(RECT);

            // Allocate a reasonable buffer upfront to avoid two calls
            // Most frames have < 100 dirty rects, so 100 * 16 bytes = 1600 bytes
            const int MaxExpectedRects = 100;
            var bufferSize = (uint)(MaxExpectedRects * rectSize);
            var dirtyRectsPtr = (RECT*)NativeMemory.Alloc(bufferSize);

            try
            {
                uint actualSize = 0;
                try
                {
                    outputDuplication.GetFrameDirtyRects(bufferSize, dirtyRectsPtr, out actualSize);
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x8007007A) // ERROR_INSUFFICIENT_BUFFER
                {
                    // Buffer too small, reallocate with the required size
                    NativeMemory.Free(dirtyRectsPtr);
                    bufferSize = actualSize;
                    dirtyRectsPtr = (RECT*)NativeMemory.Alloc(bufferSize);
                    outputDuplication.GetFrameDirtyRects(bufferSize, dirtyRectsPtr, out actualSize);
                }

                var numRects = (int)(actualSize / rectSize);
                var dirtyRects = new Rectangle[numRects];

                for (var i = 0; i < numRects; i++)
                {
                    var rect = dirtyRectsPtr[i];
                    dirtyRects[i] = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                }

                return dirtyRects;
            }
            finally
            {
                NativeMemory.Free(dirtyRectsPtr);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to get dirty rects");
            return [];
        }
    }

    private unsafe DxOutput? GetOrCreateOutput(string deviceName)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return null;

        lock (this._lock)
        {
            if (this._outputs.TryGetValue(deviceName, out var existingOutput))
            {
                return existingOutput;
            }

            try
            {
                var factoryGuid = typeof(IDXGIFactory1).GUID;
                var factoryResult = PInvoke.CreateDXGIFactory1(factoryGuid, out var factoryObj);
                if (factoryResult.Failed)
                {
                    logger.LogWarning("Failed to create DXGI Factory. HRESULT: {Result}", factoryResult);
                    return null;
                }

                var factory = (IDXGIFactory1)factoryObj;

                try
                {
                    var adapterOutput = FindOutput(factory, deviceName);
                    if (adapterOutput is null)
                    {
                        logger.LogWarning("Failed to find DXGI output for device {DeviceName}", deviceName);
                        return null;
                    }

                    var (adapter, output) = adapterOutput.Value;

                    var featureLevels = new[]
                    {
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_1,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_0,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_3,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_2,
                            D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_9_1
                        };

                    var result = PInvoke.D3D11CreateDevice(
                        adapter,
                        D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_UNKNOWN,
                        HMODULE.Null,
                        0,
                        featureLevels,
                        PInvoke.D3D11_SDK_VERSION,
                        out var device,
                        null,
                        out var deviceContext);

                    if (result.Failed)
                    {
                        logger.LogWarning("Failed to create D3D11 device. HRESULT: {Result}", result);
                        Marshal.FinalReleaseComObject(output);
                        Marshal.FinalReleaseComObject(adapter);
                        return null;
                    }

                    output.DuplicateOutput(device, out var outputDuplication);

                    if (outputDuplication is null)
                    {
                        logger.LogWarning("Failed to duplicate output for device {DeviceName}", deviceName);
                        if (deviceContext is not null)
                        {
                            Marshal.FinalReleaseComObject(deviceContext);
                        }
                        if (device is not null)
                        {
                            Marshal.FinalReleaseComObject(device);
                        }
                        Marshal.FinalReleaseComObject(output);
                        Marshal.FinalReleaseComObject(adapter);
                        return null;
                    }

                    var dxOutput = new DxOutput(deviceName, device, deviceContext, outputDuplication);
                    this._outputs[deviceName] = dxOutput;

                    Marshal.FinalReleaseComObject(output);
                    Marshal.FinalReleaseComObject(adapter);

                    return dxOutput;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(factory);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to create DXGI output for device {DeviceName}", deviceName);
                return null;
            }
        }
    }

    public void SetOutputFaulted(string deviceName)
    {
        lock (this._lock)
        {
            if (this._outputs.TryGetValue(deviceName, out var output))
            {
                output.Dispose();
                this._outputs.Remove(deviceName);
            }
        }
    }

    private static (IDXGIAdapter1 Adapter, IDXGIOutput1 Output)? FindOutput(IDXGIFactory1 factory, string deviceName)
    {
        for (uint adapterIndex = 0; factory.EnumAdapters1(adapterIndex, out var adapter).Succeeded; adapterIndex++)
        {
            for (uint outputIndex = 0; adapter.EnumOutputs(outputIndex, out var output).Succeeded; outputIndex++)
            {
                var outputDesc = output.GetDesc();
                var outputDeviceName = outputDesc.DeviceName.ToString();

                if (outputDeviceName == deviceName)
                {
                    return (adapter, (IDXGIOutput1)output);
                }

                Marshal.FinalReleaseComObject(output);
            }

            Marshal.FinalReleaseComObject(adapter);
        }

        return null;
    }

    public void Dispose()
    {
        lock (this._lock)
        {
            foreach (var output in this._outputs.Values)
            {
                output.Dispose();
            }
            this._outputs.Clear();
        }
    }

    private sealed class DxOutput : IDisposable
    {
        private ID3D11Texture2D? _cachedStagingTexture;
        private nint _cachedStagingTexturePtr;
        private int _cachedWidth;
        private int _cachedHeight;

        public string DeviceName { get; }
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext DeviceContext { get; }
        public IDXGIOutputDuplication OutputDuplication { get; }

        public DxOutput(string deviceName, ID3D11Device device, ID3D11DeviceContext deviceContext, IDXGIOutputDuplication outputDuplication)
        {
            this.DeviceName = deviceName;
            this.Device = device;
            this.DeviceContext = deviceContext;
            this.OutputDuplication = outputDuplication;
        }

        public unsafe ID3D11Texture2D GetOrCreateStagingTexture(int width, int height)
        {
            if (this._cachedStagingTexture != null && this._cachedWidth == width && this._cachedHeight == height)
            {
                return this._cachedStagingTexture;
            }

            this.DisposeStagingTexture();

            var textureDesc = new D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                SampleDesc = new DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            ID3D11Texture2D_unmanaged* stagingTexturePtr;
            this.Device.CreateTexture2D(&textureDesc, null, &stagingTexturePtr);

            if (stagingTexturePtr == null)
            {
                throw new InvalidOperationException("Failed to create staging texture");
            }

            this._cachedStagingTexturePtr = (nint)stagingTexturePtr;
            this._cachedStagingTexture = (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)stagingTexturePtr);
            this._cachedWidth = width;
            this._cachedHeight = height;

            return this._cachedStagingTexture;
        }

        private unsafe void DisposeStagingTexture()
        {
            if (this._cachedStagingTexture != null)
            {
                try
                {
                    Marshal.FinalReleaseComObject(this._cachedStagingTexture);
                }
                catch
                {
                }
                this._cachedStagingTexture = null;
            }

            if (this._cachedStagingTexturePtr != 0)
            {
                try
                {
                    var ptr = (ID3D11Texture2D_unmanaged*)this._cachedStagingTexturePtr;
                    ptr->Release();
                }
                catch
                {
                }
                this._cachedStagingTexturePtr = 0;
            }

            this._cachedWidth = 0;
            this._cachedHeight = 0;
        }

        public void Dispose()
        {
            this.DisposeStagingTexture();

            try
            {
                Marshal.FinalReleaseComObject(this.OutputDuplication);
            }
            catch
            {
            }

            try
            {
                Marshal.FinalReleaseComObject(this.DeviceContext);
            }
            catch
            {
            }

            try
            {
                Marshal.FinalReleaseComObject(this.Device);
            }
            catch
            {
            }
        }
    }
}
#endif

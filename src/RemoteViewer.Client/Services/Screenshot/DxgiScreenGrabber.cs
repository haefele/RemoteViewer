#if WINDOWS
using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Common;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace RemoteViewer.Client.Services.Screenshot;

public class DxgiScreenGrabber(ILogger<DxgiScreenGrabber> logger) : IScreenGrabber
{
    // Buffers for dirty/move rects - allocated once, kept for app lifetime
    private readonly byte[] _dirtyRectsBuffer = new byte[100 * Unsafe.SizeOf<RECT>()];
    private readonly byte[] _moveRectsBuffer = new byte[100 * Unsafe.SizeOf<DXGI_OUTDUPL_MOVE_RECT>()];

    public bool IsAvailable => OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8);
    public int Priority => 100;

    private readonly ConcurrentDictionary<string, DxOutput> _outputs = new();

    public GrabResult CaptureDisplay(Display display, bool forceKeyframe)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return new GrabResult(GrabStatus.Failure, null, null, null);

        var width = display.Bounds.Width;
        var height = display.Bounds.Height;

        try
        {
            var dxOutput = this.GetOrCreateOutput(display.Name);
            if (dxOutput is null)
            {
                logger.LogDebug("Failed to get DXGI output for display {DisplayName}, falling back to BitBlt", display.Name);
                return new GrabResult(GrabStatus.Failure, null, null, null);
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
                return new GrabResult(GrabStatus.NoChanges, null, null, null);
            }

            try
            {
                var sourceTexture = (ID3D11Texture2D)screenResource;

                if (forceKeyframe)
                {
                    return this.CaptureKeyframe(dxOutput, sourceTexture, width, height);
                }
                else
                {
                    return this.CaptureDeltaFrame(dxOutput, sourceTexture, outputDuplication, width, height);
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
            return new GrabResult(GrabStatus.NoChanges, null, null, null);
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x887A0026)
        {
            logger.LogWarning("DXGI_ERROR_ACCESS_LOST - recreating output duplication");
            this.ResetDisplay(display.Name);

            return new GrabResult(GrabStatus.Failure, null, null, null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Exception occurred while capturing display {DisplayName} with DXGI", display.Name);
            this.ResetDisplay(display.Name);

            return new GrabResult(GrabStatus.Failure, null, null, null);
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe GrabResult CaptureKeyframe(DxOutput dxOutput, ID3D11Texture2D sourceTexture, int width, int height)
    {
        var deviceContext = dxOutput.DeviceContext;
        var stagingTexture = dxOutput.GetOrCreateStagingTexture(width, height);

        deviceContext.CopyResource(stagingTexture, sourceTexture);

        D3D11_MAPPED_SUBRESOURCE mappedResource;
        deviceContext.Map(stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mappedResource);

        try
        {
            var bufferSize = width * height * 4;
            var frameMemory = RefCountedMemoryOwner<byte>.Create(bufferSize);

            fixed (byte* destPtr = frameMemory.Span)
            {
                var sourcePtr = (byte*)mappedResource.pData;

                if (width * 4 == mappedResource.RowPitch)
                {
                    Unsafe.CopyBlock(destPtr, sourcePtr, (uint)bufferSize);
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
            }

            return new GrabResult(GrabStatus.Success, frameMemory, null, null);
        }
        finally
        {
            deviceContext.Unmap(stagingTexture, 0);
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe GrabResult CaptureDeltaFrame(
        DxOutput dxOutput,
        ID3D11Texture2D sourceTexture,
        IDXGIOutputDuplication outputDuplication,
        int width,
        int height)
    {
        var deviceContext = dxOutput.DeviceContext;

        // Get dirty and move rects FIRST, before any GPU copy
        var dirtyRectangles = this.GetDirtyRects(outputDuplication);
        var moveRects = this.GetMoveRects(outputDuplication);

        if (dirtyRectangles.Length == 0 && moveRects.Length == 0)
        {
            return new GrabResult(GrabStatus.NoChanges, null, null, null);
        }

        var stagingTexture = dxOutput.GetOrCreateStagingTexture(width, height);

        deviceContext.CopyResource(stagingTexture, sourceTexture);

        // Map once, extract all dirty regions, then unmap
        D3D11_MAPPED_SUBRESOURCE mappedResource;
        deviceContext.Map(stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mappedResource);

        try
        {
            var dirtyRegions = new DirtyRegion[dirtyRectangles.Length];

            for (var i = 0; i < dirtyRectangles.Length; i++)
            {
                var rect = dirtyRectangles[i];
                var regionBufferSize = rect.Width * rect.Height * 4;
                var regionMemory = RefCountedMemoryOwner<byte>.Create(regionBufferSize);

                fixed (byte* destPtr = regionMemory.Span)
                {
                    var sourcePtr = (byte*)mappedResource.pData;

                    // Extract only this dirty region from the full frame
                    for (var y = 0; y < rect.Height; y++)
                    {
                        var destRow = destPtr + y * rect.Width * 4;
                        var sourceRow = sourcePtr + (rect.Y + y) * mappedResource.RowPitch + rect.X * 4;
                        Unsafe.CopyBlock(destRow, sourceRow, (uint)(rect.Width * 4));
                    }
                }

                dirtyRegions[i] = new DirtyRegion(rect.X, rect.Y, rect.Width, rect.Height, regionMemory);
            }

            return new GrabResult(GrabStatus.Success, null, dirtyRegions, moveRects);
        }
        finally
        {
            deviceContext.Unmap(stagingTexture, 0);
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe Rectangle[] GetDirtyRects(IDXGIOutputDuplication outputDuplication)
    {
        try
        {
            fixed (byte* bufferPtr = this._dirtyRectsBuffer)
            {
                var dirtyRectsPtr = (RECT*)bufferPtr;
                uint actualSize = 0;

                try
                {
                    outputDuplication.GetFrameDirtyRects((uint)this._dirtyRectsBuffer.Length, dirtyRectsPtr, out actualSize);
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x8007007A) // ERROR_INSUFFICIENT_BUFFER
                {
                    logger.LogDebug("Dirty rects buffer too small ({Capacity} bytes, needed {Required}), skipping dirty rects", this._dirtyRectsBuffer.Length, actualSize);
                    return []; // Buffer too small - return empty and let it trigger a keyframe
                }

                var numRects = (int)(actualSize / (uint)sizeof(RECT));
                var dirtyRects = new Rectangle[numRects];

                for (var i = 0; i < numRects; i++)
                {
                    var rect = dirtyRectsPtr[i];
                    dirtyRects[i] = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                }

                return dirtyRects;
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to get dirty rects");
            return [];
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe MoveRegion[] GetMoveRects(IDXGIOutputDuplication outputDuplication)
    {
        try
        {
            fixed (byte* bufferPtr = this._moveRectsBuffer)
            {
                var moveRectsPtr = (DXGI_OUTDUPL_MOVE_RECT*)bufferPtr;
                uint actualSize = 0;

                try
                {
                    outputDuplication.GetFrameMoveRects((uint)this._moveRectsBuffer.Length, moveRectsPtr, out actualSize);
                }
                catch (COMException ex) when ((uint)ex.HResult == 0x8007007A) // ERROR_INSUFFICIENT_BUFFER
                {
                    logger.LogDebug("Move rects buffer too small ({Capacity} bytes, needed {Required}), skipping move rects", this._moveRectsBuffer.Length, actualSize);
                    return [];
                }

                var numRects = (int)(actualSize / (uint)sizeof(DXGI_OUTDUPL_MOVE_RECT));
                var moveRects = new MoveRegion[numRects];

                for (var i = 0; i < numRects; i++)
                {
                    var moveRect = moveRectsPtr[i];
                    var destRect = moveRect.DestinationRect;

                    moveRects[i] = new MoveRegion(
                        moveRect.SourcePoint.X,
                        moveRect.SourcePoint.Y,
                        destRect.left,
                        destRect.top,
                        destRect.right - destRect.left,
                        destRect.bottom - destRect.top);
                }

                return moveRects;
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to get move rects");
            return [];
        }
    }

    private DxOutput? GetOrCreateOutput(string deviceName)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return null;

        // Try to get existing output first (fast path)
        if (this._outputs.TryGetValue(deviceName, out var existingOutput))
            return existingOutput;

        // Create new output
        var newOutput = this.CreateOutput(deviceName);
        if (newOutput is null)
            return null;

        // Try to add it - if another thread added one first, dispose ours and use theirs
        var output = this._outputs.GetOrAdd(deviceName, newOutput);
        if (output == newOutput)
        {
            return newOutput;
        }
        else
        {
            newOutput.Dispose();
            return output;
        }
    }

    [SupportedOSPlatform("windows8.0")]
    private unsafe DxOutput? CreateOutput(string deviceName)
    {
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

    public void ResetDisplay(string displayName)
    {
        if (this._outputs.TryRemove(displayName, out var output))
        {
            output.Dispose();
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
        foreach (var output in this._outputs.Values)
        {
            output.Dispose();
        }
        this._outputs.Clear();
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

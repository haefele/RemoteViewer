using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Dxgi.Common;

namespace RemoteViewer.WinServ.Services;

public class DxgiScreenGrabber(ILogger<DxgiScreenGrabber> logger)
{
    private readonly Dictionary<string, DxOutput> _outputs = new();
    private readonly HashSet<string> _faultedDevices = new();
    private readonly object _lock = new();

    public unsafe CaptureResult CaptureDisplay(Display display)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return CaptureResult.Failure;

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

            DXGI_OUTDUPL_FRAME_INFO frameInfo;
            outputDuplication.AcquireNextFrame(0, out frameInfo, out var screenResource);

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

                D3D11_MAPPED_SUBRESOURCE mappedResource;
                deviceContext.Map(stagingTexture, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mappedResource);

                try
                {
                    var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                    var destPtr = (byte*)skBitmap.GetPixels();
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

                    var dirtyRects = GetDirtyRects(outputDuplication);
                    return CaptureResult.Ok(skBitmap, dirtyRects);
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
            uint bufferSizeNeeded = 0;

            try
            {
                outputDuplication.GetFrameDirtyRects(0, null, out bufferSizeNeeded);
            }
            catch
            {
                return Array.Empty<Rectangle>();
            }

            if (bufferSizeNeeded == 0)
            {
                return Array.Empty<Rectangle>();
            }

            var numRects = (int)(bufferSizeNeeded / rectSize);
            var dirtyRects = new Rectangle[numRects];
            var dirtyRectsPtr = (RECT*)NativeMemory.Alloc(bufferSizeNeeded);

            try
            {
                outputDuplication.GetFrameDirtyRects(bufferSizeNeeded, dirtyRectsPtr, out bufferSizeNeeded);

                for (var i = 0; i < numRects; i++)
                {
                    var rect = dirtyRectsPtr[i];
                    dirtyRects[i] = new Rectangle(rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top);
                }
            }
            finally
            {
                NativeMemory.Free(dirtyRectsPtr);
            }

            return dirtyRects;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to get dirty rects");
            return Array.Empty<Rectangle>();
        }
    }

    private unsafe DxOutput? GetOrCreateOutput(string deviceName)
    {
        if (!OperatingSystem.IsOSPlatformVersionAtLeast("windows", 8))
            return null;

        lock (_lock)
        {
            if (_faultedDevices.Contains(deviceName))
            {
                return null;
            }

            if (_outputs.TryGetValue(deviceName, out var existingOutput))
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
                    _outputs[deviceName] = dxOutput;

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
                _faultedDevices.Add(deviceName);
                return null;
            }
        }
    }

    public void SetOutputFaulted(string deviceName)
    {
        lock (_lock)
        {
            if (_outputs.TryGetValue(deviceName, out var output))
            {
                output.Dispose();
                _outputs.Remove(deviceName);
            }
            _faultedDevices.Add(deviceName);
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
        lock (_lock)
        {
            foreach (var output in _outputs.Values)
            {
                output.Dispose();
            }
            _outputs.Clear();
        }
    }

    private sealed class DxOutput : IDisposable
    {
        private ID3D11Texture2D? _cachedStagingTexture;
        private nint _cachedStagingTexturePtr;

        public string DeviceName { get; }
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext DeviceContext { get; }
        public IDXGIOutputDuplication OutputDuplication { get; }

        public DxOutput(string deviceName, ID3D11Device device, ID3D11DeviceContext deviceContext, IDXGIOutputDuplication outputDuplication)
        {
            DeviceName = deviceName;
            Device = device;
            DeviceContext = deviceContext;
            OutputDuplication = outputDuplication;
        }

        public unsafe ID3D11Texture2D GetOrCreateStagingTexture(int width, int height)
        {
            if (_cachedStagingTexture != null)
            {
                return _cachedStagingTexture;
            }

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
            Device.CreateTexture2D(&textureDesc, null, &stagingTexturePtr);

            if (stagingTexturePtr == null)
            {
                throw new InvalidOperationException("Failed to create staging texture");
            }

            _cachedStagingTexturePtr = (nint)stagingTexturePtr;
            _cachedStagingTexture = (ID3D11Texture2D)Marshal.GetObjectForIUnknown((nint)stagingTexturePtr);

            return _cachedStagingTexture;
        }

        public void Dispose()
        {
            try
            {
                if (_cachedStagingTexture != null)
                {
                    Marshal.FinalReleaseComObject(_cachedStagingTexture);
                    _cachedStagingTexture = null;
                }
            }
            catch
            {
            }

            unsafe
            {
                try
                {
                    if (_cachedStagingTexturePtr != 0)
                    {
                        var ptr = (ID3D11Texture2D_unmanaged*)_cachedStagingTexturePtr;
                        ptr->Release();
                        _cachedStagingTexturePtr = 0;
                    }
                }
                catch
                {
                }
            }

            try
            {
                Marshal.FinalReleaseComObject(OutputDuplication);
            }
            catch
            {
            }

            try
            {
                Marshal.FinalReleaseComObject(DeviceContext);
            }
            catch
            {
            }

            try
            {
                Marshal.FinalReleaseComObject(Device);
            }
            catch
            {
            }
        }
    }
}

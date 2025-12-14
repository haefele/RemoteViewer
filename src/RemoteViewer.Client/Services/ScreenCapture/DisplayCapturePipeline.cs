using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.Screenshot;
using RemoteViewer.Client.Services.VideoCodec;
using RemoteViewer.Server.SharedAPI.Protocol;

namespace RemoteViewer.Client.Services.ScreenCapture;

public sealed class DisplayCapturePipeline : IDisposable
{
    private readonly Display _display;
    private readonly Connection _connection;
    private readonly IScreenshotService _screenshotService;
    private readonly ScreenEncoder _screenEncoder;
    private readonly ILogger<DisplayCapturePipeline> _logger;
    private readonly Func<int> _getTargetFps;

    private readonly Channel<CapturedFrame> _captureToEncodeChannel;
    private readonly Channel<EncodedFrame> _encodeToSendChannel;

    private readonly CancellationTokenSource _pipelineCts;
    private readonly Task _captureTask;
    private readonly Task _encodeTask;
    private readonly Task _sendTask;

    private ulong _frameNumber;
    private int _disposed;

    public bool IsFaulted { get; private set; }

    public DisplayCapturePipeline(
        Display display,
        Connection connection,
        IScreenshotService screenshotService,
        ScreenEncoder screenEncoder,
        Func<int> getTargetFps,
        ILogger<DisplayCapturePipeline> logger)
    {
        this._display = display;
        this._connection = connection;
        this._screenshotService = screenshotService;
        this._screenEncoder = screenEncoder;
        this._getTargetFps = getTargetFps;
        this._logger = logger;

        this._logger.PipelineStarted(display.Name);

        this._captureToEncodeChannel = Channel.CreateBounded<CapturedFrame>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            static frame => frame.Dispose());

        this._encodeToSendChannel = Channel.CreateBounded<EncodedFrame>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            },
            static frame => frame.Dispose());

        this._pipelineCts = new CancellationTokenSource();
        this._captureTask = Task.Run(() => this.CaptureLoopAsync(this._pipelineCts.Token));
        this._encodeTask = Task.Run(() => this.EncodeLoopAsync(this._pipelineCts.Token));
        this._sendTask = Task.Run(() => this.SendLoopAsync(this._pipelineCts.Token));
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        this._logger.CaptureLoopStarted(this._display.Name);

        var frameStartTimestamp = Stopwatch.GetTimestamp();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frameNumber = this._frameNumber++;

                var grabResult = this._screenshotService.CaptureDisplay(this._display);
                if (grabResult.Status == GrabStatus.Success)
                {
                    var frame = new CapturedFrame(frameNumber, grabResult);

                    if (this._captureToEncodeChannel.Writer.TryWrite(frame) is false)
                    {
                        this._logger.CapturedFrameDropped(this._display.Name, frameNumber);
                        frame.Dispose();
                    }

                    // Frame rate throttling
                    var minFrameInterval = TimeSpan.FromMilliseconds(1000.0 / this._getTargetFps());
                    var elapsed = Stopwatch.GetElapsedTime(frameStartTimestamp);
                    var sleepTime = minFrameInterval - elapsed;
                    if (sleepTime > TimeSpan.Zero)
                    {
                        await Task.Delay(sleepTime, ct);
                    }

                    frameStartTimestamp = Stopwatch.GetTimestamp();
                }
                else if (grabResult.Status == GrabStatus.NoChanges)
                {
                    grabResult.Dispose();
                    await Task.Delay(1, ct);
                }
                else
                {
                    this._logger.ScreenGrabFailed(this._display.Name, grabResult.Status);
                    grabResult.Dispose();
                    await Task.Delay(10, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            this._logger.CaptureLoopFailed(ex, this._display.Name);
            this.IsFaulted = true;
        }
        finally
        {
            this._captureToEncodeChannel.Writer.Complete();
            this._logger.CaptureLoopCompleted(this._display.Name);
        }
    }

    private async Task EncodeLoopAsync(CancellationToken ct)
    {
        this._logger.EncodeLoopStarted(this._display.Name);

        try
        {
            await foreach (var capturedFrame in this._captureToEncodeChannel.Reader.ReadAllAsync(ct))
            {
                using (capturedFrame)
                {
                    var (codec, encodedRegions) = this._screenEncoder.ProcessFrame(
                        capturedFrame.GrabResult,
                        this._display.Bounds.Width,
                        this._display.Bounds.Height);

                    var frame = new EncodedFrame(capturedFrame.FrameNumber, codec, encodedRegions);

                    if (this._encodeToSendChannel.Writer.TryWrite(frame) is false)
                    {
                        this._logger.EncodedFrameDropped(this._display.Name, capturedFrame.FrameNumber);
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            this._logger.EncodeLoopFailed(ex, this._display.Name);
            this.IsFaulted = true;
        }
        finally
        {
            this._encodeToSendChannel.Writer.Complete();
            this._logger.EncodeLoopCompleted(this._display.Name);
        }
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        this._logger.SendLoopStarted(this._display.Name);
        var reader = this._encodeToSendChannel.Reader;

        try
        {
            await foreach (var encodedFrame in reader.ReadAllAsync(ct))
            {
                using (encodedFrame)
                {
                    var regions = new FrameRegion[encodedFrame.Regions.Length];
                    for (var i = 0; i < encodedFrame.Regions.Length; i++)
                    {
                        var region = encodedFrame.Regions[i];
                        regions[i] = new FrameRegion(
                            region.IsKeyframe,
                            region.X,
                            region.Y,
                            region.Width,
                            region.Height,
                            region.JpegData.Memory);
                    }

                    await this._connection.SendFrameAsync(
                        this._display.Name,
                        encodedFrame.FrameNumber,
                        encodedFrame.Codec,
                        regions);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            this._logger.SendLoopFailed(ex, this._display.Name);
            this.IsFaulted = true;
        }
        finally
        {
            this._logger.SendLoopCompleted(this._display.Name);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref this._disposed, 1) == 1)
            return;

        this._pipelineCts.Cancel();

        var tasksCompleted = Task.WaitAll(
            [this._captureTask, this._encodeTask, this._sendTask],
            TimeSpan.FromSeconds(2));

        if (!tasksCompleted)
        {
            this._logger.DisposeTimedOut(this._display.Name);
        }

        // Drain any remaining frames from channels to prevent resource leaks
        this.DrainChannel(this._captureToEncodeChannel.Reader);
        this.DrainChannel(this._encodeToSendChannel.Reader);

        this._pipelineCts.Dispose();
        this._logger.PipelineStopped(this._display.Name);
    }

    private void DrainChannel<T>(ChannelReader<T> reader) where T : IDisposable
    {
        while (reader.TryRead(out var item))
        {
            item.Dispose();
        }
    }

    private readonly record struct CapturedFrame(
        ulong FrameNumber,
        GrabResult GrabResult) : IDisposable
    {
        public void Dispose() => this.GrabResult.Dispose();
    }

    private readonly record struct EncodedFrame(
        ulong FrameNumber,
        FrameCodec Codec,
        EncodedRegion[] Regions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var region in this.Regions)
            {
                region.JpegData.Dispose();
            }
        }
    }
}

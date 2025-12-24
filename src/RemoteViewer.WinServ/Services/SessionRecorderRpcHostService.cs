using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Options;
using Nerdbank.Streams;
using RemoteViewer.WinServ.Options;
using StreamJsonRpc;

namespace RemoteViewer.WinServ.Services;

public class SessionRecorderRpcHostService(
    ILogger<SessionRecorderRpcHostService> logger,
    IOptions<RemoteViewerOptions> options,
    IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only run in SessionRecorder mode
        if (options.Value.Mode is not RemoteViewerMode.SessionRecorder)
            return;

        var sessionId = GetCurrentSessionId();
        var pipeName = $"RemoteViewer.Session.{sessionId}";

        logger.LogInformation("Starting RPC server on pipe: {PipeName}", pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 0,
                    outBufferSize: 0,
                    pipeSecurity: CreatePipeSecurity());

                logger.LogDebug("Waiting for client connection on pipe: {PipeName}", pipeName);

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                logger.LogInformation("Client connected to RPC server");

                // Handle this client in a separate task
                _ = this.HandleClientAsync(pipeServer, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in RPC server accept loop");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken stoppingToken)
    {
        try
        {
            // Create the RPC server target
            var rpcTarget = serviceProvider.GetRequiredService<SessionRecorderRpcServer>();

            // Configure MessagePack formatter for StreamJsonRpc
            var formatter = new MessagePackFormatter();
            var handler = new LengthHeaderMessageHandler(pipeServer.UsePipe(cancellationToken: stoppingToken), formatter);
            var jsonRpc = new JsonRpc(handler, rpcTarget);

            jsonRpc.Disconnected += (sender, args) =>
            {
                logger.LogInformation("RPC client disconnected: {Reason}", args.Reason);
            };

            jsonRpc.StartListening();

            // Wait for disconnection or cancellation
            await jsonRpc.Completion.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling RPC client");
        }
        finally
        {
            await pipeServer.DisposeAsync();
        }
    }

    private static uint GetCurrentSessionId()
    {
        return (uint)Process.GetCurrentProcess().SessionId;
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        // Allow authenticated users (desktop app) to connect
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }
}

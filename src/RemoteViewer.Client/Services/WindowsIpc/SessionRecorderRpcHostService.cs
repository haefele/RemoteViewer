using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.WindowsIpc;

public class SessionRecorderRpcHostService(
    ILogger<SessionRecorderRpcHostService> logger,
    IServiceProvider serviceProvider) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sessionId = GetCurrentSessionId();
        var pipeName = $"RemoteViewer.Session.{sessionId}";

        logger.LogInformation("Starting RPC server on pipe: {PipeName}", pipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                // Use large buffers for efficient frame data transfer (default 4KB is way too small)
                const int pipeBufferSize = 1024 * 1024; // 1MB

                pipeServer = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: pipeBufferSize,
                    outBufferSize: pipeBufferSize,
                    pipeSecurity: CreatePipeSecurity());

                logger.LogDebug("Waiting for client connection on pipe: {PipeName}", pipeName);

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                logger.LogInformation("Client connected to RPC server");

                // Handle client and wait for it to complete before accepting next connection
                await this.HandleClientAsync(pipeServer, stoppingToken);
                pipeServer = null; // HandleClientAsync takes ownership and disposes
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
            finally
            {
                if (pipeServer is not null)
                    await pipeServer.DisposeAsync();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipeServer, CancellationToken stoppingToken)
    {
        try
        {
            // Create the RPC server target
            var rpcTarget = serviceProvider.GetRequiredService<SessionRecorderRpcServer>();

            // Configure Nerdbank.MessagePack formatter for StreamJsonRpc
            var formatter = new NerdbankMessagePackFormatter
            {
                TypeShapeProvider = IpcWitness.GeneratedTypeShapeProvider
            };
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

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Nerdbank.Streams;
using StreamJsonRpc;

namespace RemoteViewer.Client.Services.WinServiceIpc;

public class WinServiceRpcHostService(
    ILogger<WinServiceRpcHostService> logger,
    IServiceProvider serviceProvider) : BackgroundService
{
    private const string PipeName = "RemoteViewer.WinService";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.EnsureSoftwareSasEnabled();

        logger.LogInformation("Starting WinService RPC server on pipe: {PipeName}", PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 64 * 1024,
                    outBufferSize: 64 * 1024,
                    pipeSecurity: CreatePipeSecurity());

                logger.LogDebug("Waiting for client connection on pipe: {PipeName}", PipeName);

                await pipeServer.WaitForConnectionAsync(stoppingToken);

                logger.LogInformation("Client connected to WinService RPC server");

                await this.HandleClientAsync(pipeServer, stoppingToken);
                pipeServer = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in WinService RPC server accept loop");
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
            var rpcTarget = serviceProvider.GetRequiredService<WinServiceRpcServer>();

            var formatter = new NerdbankMessagePackFormatter
            {
                TypeShapeProvider = WinServiceIpcWitness.GeneratedTypeShapeProvider
            };
            var handler = new LengthHeaderMessageHandler(pipeServer.UsePipe(cancellationToken: stoppingToken), formatter);
            var jsonRpc = new JsonRpc(handler, rpcTarget);

            jsonRpc.Disconnected += (sender, args) =>
            {
                logger.LogInformation("WinService RPC client disconnected: {Reason}", args.Reason);
            };

            jsonRpc.StartListening();

            await jsonRpc.Completion.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling WinService RPC client");
        }
        finally
        {
            await pipeServer.DisposeAsync();
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();

        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return security;
    }

    private void EnsureSoftwareSasEnabled()
    {
        // SendSAS requires this registry setting to be enabled
        // https://learn.microsoft.com/en-us/archive/blogs/technet/itasupport/sendsas-step-by-step
        // http://www.atelierweb.com/index.php/ctrlaltdel/
        const string KeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        const string ValueName = "SoftwareSASGeneration";
        const int ValueForServices = 1; // 1 = Services, 2 = UI Access apps, 3 = Both

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyPath, writable: true);
            if (key is null)
            {
                logger.LogWarning("Could not open registry key for SoftwareSASGeneration");
                return;
            }

            var currentValue = key.GetValue(ValueName) as int?;
            if (currentValue is null or 0)
            {
                key.SetValue(ValueName, ValueForServices, RegistryValueKind.DWord);
                logger.LogInformation("Enabled SoftwareSASGeneration registry setting for SendSAS support");
            }
            else if ((currentValue & 1) == 0)
            {
                // Value exists but doesn't include services (bit 0), add it
                key.SetValue(ValueName, currentValue.Value | 1, RegistryValueKind.DWord);
                logger.LogInformation("Updated SoftwareSASGeneration registry setting to include services");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set SoftwareSASGeneration registry value");
        }
    }
}

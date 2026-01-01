using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.WindowsSession;

namespace RemoteViewer.Client.Services.WinServiceIpc;

public partial class WinServiceRpcServer(
    IWin32SessionService sessionService,
    ILogger<WinServiceRpcServer> logger) : IWinServiceRpc
{
    private readonly HashSet<string> _authenticatedConnections = [];
    private readonly object _connectionsLock = new();

    public async Task<AuthenticateResult> Authenticate(string token, CancellationToken ct)
    {
        try
        {
            var connection = new HubConnectionBuilder()
                .WithUrl($"{ConnectionHubClient.BaseUrl}/connection", options =>
                {
                    options.Headers.Add("X-Ipc-Token", token);
                })
                .Build();

            var validationResult = new TaskCompletionSource<string?>();

            connection.On<string?>("IpcTokenValidated", connectionId => validationResult.TrySetResult(connectionId));
            connection.Closed += _ =>
            {
                validationResult.TrySetResult(null);
                return Task.CompletedTask;
            };

            await connection.StartAsync(ct);

            var validatedConnectionId = await validationResult.Task.WaitAsync(ct);

            await connection.DisposeAsync();

            if (validatedConnectionId is not null)
            {
                lock (this._connectionsLock)
                {
                    this._authenticatedConnections.Add(validatedConnectionId);
                }
                this.ConnectionAuthenticated(validatedConnectionId);
                return new AuthenticateResult(true, null);
            }

            this.AuthenticationRejected();
            return new AuthenticateResult(false, "Invalid or expired token");
        }
        catch (Exception ex)
        {
            this.AuthenticationFailed(ex);
            return new AuthenticateResult(false, "Failed to validate token");
        }
    }

    private void ValidateConnectionId(string connectionId)
    {
        lock (this._connectionsLock)
        {
            if (!this._authenticatedConnections.Contains(connectionId))
                throw new InvalidOperationException("Connection ID not authenticated");
        }
    }

    public Task<bool> SendSecureAttentionSequence(string connectionId, uint sessionId, CancellationToken ct)
    {
        this.ValidateConnectionId(connectionId);
        return Task.FromResult(sessionService.SendSasToSession(sessionId));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connection authenticated: {ConnectionId}")]
    private partial void ConnectionAuthenticated(string connectionId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Authentication rejected: invalid or expired token")]
    private partial void AuthenticationRejected();

    [LoggerMessage(Level = LogLevel.Error, Message = "Authentication failed")]
    private partial void AuthenticationFailed(Exception ex);
}

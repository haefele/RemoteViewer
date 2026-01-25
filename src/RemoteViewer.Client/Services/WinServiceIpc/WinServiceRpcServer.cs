using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RemoteViewer.Client.Services.HubClient;
using RemoteViewer.Client.Services.WindowsSession;
using RemoteViewer.Shared;
using System.Net.Http.Json;

namespace RemoteViewer.Client.Services.WinServiceIpc;

public partial class WinServiceRpcServer(
    IWin32SessionService sessionService,
    IOptions<ConnectionHubClientOptions> hubClientOptions,
    HttpClient httpClient,
    ILogger<WinServiceRpcServer> logger) : IWinServiceRpc
{
    private readonly HashSet<string> _authenticatedConnections = [];
    private readonly object _connectionsLock = new();

    public async Task<AuthenticateResult> Authenticate(string token, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.PostAsJsonAsync(
                $"{hubClientOptions.Value.BaseUrl}/api/ipc/validate",
                new IpcTokenValidateRequest(token),
                ct);
            response.EnsureSuccessStatusCode();
            var validation = await response.Content.ReadFromJsonAsync<IpcTokenValidateResponse>(cancellationToken: ct);
            if (validation is not null && validation.Success && !string.IsNullOrWhiteSpace(validation.ConnectionId))
            {
                lock (this._connectionsLock)
                {
                    this._authenticatedConnections.Add(validation.ConnectionId);
                }
                this.ConnectionAuthenticated(validation.ConnectionId);
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

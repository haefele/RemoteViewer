using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RemoteViewer.Server.Services;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Controllers;

[ApiController]
[Route("api/ipc")]
public sealed class IpcTokenController : ControllerBase
{
    private readonly IIpcTokenService _ipcTokenService;
    private readonly IConnectionsService _connectionsService;
    private readonly ILogger<IpcTokenController> _logger;

    public IpcTokenController(
        IIpcTokenService ipcTokenService,
        IConnectionsService connectionsService,
        ILogger<IpcTokenController> logger)
    {
        this._ipcTokenService = ipcTokenService;
        this._connectionsService = connectionsService;
        this._logger = logger;
    }
    [Authorize]
    [HttpPost("token")]
    public async Task<ActionResult<IpcTokenResponse>> GenerateToken([FromBody] IpcTokenRequest request)
    {
        var clientGuid = this.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(clientGuid))
        {
            return this.Unauthorized();
        }

        var signalrConnectionId = await this._connectionsService.GetSignalrConnectionIdAsync(clientGuid);
        if (string.IsNullOrWhiteSpace(signalrConnectionId))
        {
            return this.Unauthorized();
        }

        if (await this._connectionsService.IsPresenterOfConnection(signalrConnectionId, request.ConnectionId) is false)
        {
            this._logger.LogWarning("IPC token request rejected - not presenter. ConnectionId: {ConnectionId}, SignalR: {SignalrConnectionId}",
                request.ConnectionId,
                signalrConnectionId);
            return this.Ok(new IpcTokenResponse(false, null, "Not presenter"));
        }

        var token = this._ipcTokenService.GenerateToken(request.ConnectionId);
        return this.Ok(new IpcTokenResponse(true, token, null));
    }

    [AllowAnonymous]
    [HttpPost("validate")]
    public ActionResult<IpcTokenValidateResponse> ValidateToken([FromBody] IpcTokenValidateRequest request)
    {
        var connectionId = this._ipcTokenService.ValidateAndConsumeToken(request.Token);
        if (connectionId is null)
        {
            return this.Ok(new IpcTokenValidateResponse(false, null, "Invalid or expired token"));
        }

        return this.Ok(new IpcTokenValidateResponse(true, connectionId, null));
    }
}

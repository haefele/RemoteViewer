using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using RemoteViewer.Server.Orleans.Grains;
using RemoteViewer.Shared;

namespace RemoteViewer.Server.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IGrainFactory _grainFactory;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthController> _logger;
    private static readonly TimeSpan s_authTimeout = TimeSpan.FromSeconds(10);

    public AuthController(
        IGrainFactory grainFactory,
        TimeProvider timeProvider,
        IConfiguration configuration,
        ILogger<AuthController> logger)
    {
        this._grainFactory = grainFactory;
        this._timeProvider = timeProvider;
        this._configuration = configuration;
        this._logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<ClientRegistrationResponse>> Register([FromBody] ClientRegistrationRequest request)
    {
        var identityGrain = this._grainFactory.GetGrain<IClientIdentityGrain>(request.ClientGuid);
        var registered = await identityGrain.RegisterAsync(request.PublicKeyBase64, request.KeyFormat);
        if (registered)
        {
            return this.Ok(new ClientRegistrationResponse(true, null));
        }

        return this.Ok(new ClientRegistrationResponse(false, "Registration failed"));
    }

    [AllowAnonymous]
    [HttpPost("nonce")]
    public async Task<ActionResult<ClientAuthChallenge>> RequestNonce([FromBody] ClientAuthNonceRequest request)
    {
        var identityGrain = this._grainFactory.GetGrain<IClientIdentityGrain>(request.ClientGuid);
        if (await identityGrain.IsRegisteredAsync() is false)
        {
            this._logger.LogWarning("Auth nonce request failed: client not registered. ClientGuid: {ClientGuid}", request.ClientGuid);
            return this.Ok(new ClientAuthChallenge(null, 0, "Client not registered"));
        }

        var authGrain = this._grainFactory.GetGrain<IAuthSessionGrain>(request.ClientGuid);
        var nonce = await authGrain.IssueNonceAsync(request.ClientGuid);
        var expiresAt = this._timeProvider.GetUtcNow().Add(s_authTimeout).ToUnixTimeMilliseconds();
        return this.Ok(new ClientAuthChallenge(nonce, expiresAt, null));
    }

    [AllowAnonymous]
    [HttpPost("authenticate")]
    public async Task<ActionResult<ClientAuthTokenResponse>> Authenticate([FromBody] ClientAuthRequest request)
    {
        var serverVersion = ThisAssembly.AssemblyInformationalVersion;
        if (string.IsNullOrWhiteSpace(request.ClientVersion) ||
            string.Equals(request.ClientVersion, serverVersion, StringComparison.OrdinalIgnoreCase) is false)
        {
            this._logger.LogWarning("Client version mismatch. ClientVersion: {ClientVersion}, ServerVersion: {ServerVersion}, ClientGuid: {ClientGuid}",
                request.ClientVersion,
                serverVersion,
                request.ClientGuid);
            return this.Ok(new ClientAuthTokenResponse(false, null, null, "Client version mismatch", serverVersion));
        }

        var authGrain = this._grainFactory.GetGrain<IAuthSessionGrain>(request.ClientGuid);
        var success = await authGrain.TryCompleteAsync(request.ClientGuid, request.Signature);
        if (!success)
        {
            this._logger.LogWarning("Client authentication failed. ClientGuid: {ClientGuid}", request.ClientGuid);
            return this.Ok(new ClientAuthTokenResponse(false, null, null, "Invalid signature", serverVersion));
        }

        var tokenConfig = this._configuration.GetSection("Jwt");
        var issuer = tokenConfig["Issuer"];
        var audience = tokenConfig["Audience"];
        var signingKey = tokenConfig["SigningKey"];

        if (string.IsNullOrWhiteSpace(issuer) ||
            string.IsNullOrWhiteSpace(audience) ||
            string.IsNullOrWhiteSpace(signingKey))
        {
            this._logger.LogError("JWT configuration missing - ensure Jwt:Issuer, Jwt:Audience, and Jwt:SigningKey are set");
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }

        var now = this._timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(15);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, request.ClientGuid),
            new Claim("client_guid", request.ClientGuid),
            new Claim("display_name", request.DisplayName ?? string.Empty),
            new Claim("client_version", request.ClientVersion)
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            now.UtcDateTime,
            expiresAt.UtcDateTime,
            signingCredentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        this._logger.LogInformation("Client authenticated. ClientGuid: {ClientGuid}", request.ClientGuid);

        return this.Ok(new ClientAuthTokenResponse(true, tokenValue, expiresAt.ToUnixTimeMilliseconds(), null, serverVersion));
    }
}

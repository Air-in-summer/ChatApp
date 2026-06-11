using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using MultiRoomChatWebApp.Server.Modules.Auth.Core;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Interfaces;
using MultiRoomChatWebApp.Server.Modules.Auth.Core.Options;

namespace MultiRoomChatWebApp.Server.Modules.Auth.Authentication;

/// <summary>
/// Xac thuc request bang opaque session cookie va du lieu session phia server.
/// </summary>
public sealed class BffSessionAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IAuthSessionService _authSessionService;
    private readonly BffAuthOptions _bffOptions;

    public BffSessionAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IAuthSessionService authSessionService,
        IOptions<BffAuthOptions> bffOptions)
        : base(schemeOptions, logger, encoder)
    {
        _authSessionService = authSessionService;
        _bffOptions = bffOptions.Value;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_bffOptions.Enabled)
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Cookies.TryGetValue(_bffOptions.CookieName, out var rawSessionToken) ||
            string.IsNullOrWhiteSpace(rawSessionToken))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await _authSessionService.ValidateSessionAsync(
            rawSessionToken,
            Context.RequestAborted);

        if (session == null)
        {
            return AuthenticateResult.Fail("BFF session is invalid or expired.");
        }

        var claims = session.Claims.ToList();
        claims.Add(new Claim(BffAuthDefaults.SessionIdClaim, session.SessionId.ToString()));

        var identity = new ClaimsIdentity(
            claims,
            BffAuthDefaults.SessionScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var expiresAtUtc = session.ExpiresAtUtc;

        if (ShouldRenewSession(expiresAtUtc))
        {
            var renewal = await _authSessionService.RenewSessionAsync(
                rawSessionToken,
                Context.RequestAborted);

            if (renewal != null)
            {
                expiresAtUtc = renewal.ExpiresAtUtc;
                BffSessionCookie.Append(
                    Response,
                    _bffOptions,
                    rawSessionToken,
                    expiresAtUtc);
            }
        }

        var properties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
            IsPersistent = true
        };

        return AuthenticateResult.Success(
            new AuthenticationTicket(
                principal,
                properties,
                BffAuthDefaults.SessionScheme));
    }

    private bool ShouldRenewSession(DateTime expiresAtUtc)
    {
        if (!_bffOptions.SlidingExpiration || Response.HasStarted)
        {
            return false;
        }

        var remaining = expiresAtUtc - DateTime.UtcNow;
        return remaining > TimeSpan.Zero &&
               remaining <= TimeSpan.FromMinutes(_bffOptions.RenewalThresholdMinutes);
    }

}

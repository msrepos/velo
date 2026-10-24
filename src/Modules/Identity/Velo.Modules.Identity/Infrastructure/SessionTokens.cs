using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;

namespace Velo.Modules.Identity.Infrastructure;

public sealed class SessionTokens(IOptionsMonitor<BearerTokenOptions> options, TimeProvider time)
{
    public const string SessionClaim = "velo_session";
    public const string VersionClaim = "velo_refresh_version";
    public const string Scheme = "Velo.Session";

    // Use the framework's separate access/refresh ticket protectors, not a custom JWT format.
    public TokenResponse Issue(ApplicationUser user, AuthSession session,
        IReadOnlyCollection<string>? permissions = null)
    {
        var settings = options.Get(IdentityConstants.BearerScheme);
        var now = time.GetUtcNow();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new Claim(ClaimTypes.Name, user.PhoneNumber!),
            new Claim(ClaimTypes.Role, "Customer"),
            new Claim(SessionClaim, session.Id.ToString("D")),
            new Claim(VersionClaim, session.RefreshVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim("velo_token_nonce", Guid.NewGuid().ToString("N"))
        ], IdentityConstants.BearerScheme));
        if (session.IsStaff) ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Staff"));
        if (permissions is not null)
            ((ClaimsIdentity)principal.Identity!).AddClaims(permissions.Select(x => new Claim("permission", x)));
        var accessExpiry = now + settings.BearerTokenExpiration;
        if (accessExpiry > session.ExpiresAt) accessExpiry = session.ExpiresAt;
        var accessTicket = new AuthenticationTicket(principal,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = accessExpiry },
            IdentityConstants.BearerScheme + ":AccessToken");
        var refreshTicket = new AuthenticationTicket(principal,
            new AuthenticationProperties { IssuedUtc = now, ExpiresUtc = session.ExpiresAt },
            IdentityConstants.BearerScheme + ":RefreshToken");
        var refresh = settings.RefreshTokenProtector.Protect(refreshTicket);
        session.RefreshTokenHash = AuthCryptography.HashToken(refresh);

        return new TokenResponse("Bearer", settings.BearerTokenProtector.Protect(accessTicket),
            (long)(accessExpiry - now).TotalSeconds, refresh, session.Id);
    }

    public AuthenticationTicket? ReadRefresh(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 16_384) return null;
        var ticket = options.Get(IdentityConstants.BearerScheme).RefreshTokenProtector.Unprotect(token);
        return ticket?.Properties.ExpiresUtc > time.GetUtcNow() ? ticket : null;
    }
}

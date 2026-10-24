using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Infrastructure.Persistence;

namespace Velo.Modules.Identity.Infrastructure;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IdentityDbContext db, TimeProvider time)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var bearer = await Context.AuthenticateAsync(IdentityConstants.BearerScheme);
        if (!bearer.Succeeded) return bearer;
        if (!Guid.TryParse(bearer.Principal!.FindFirstValue(SessionTokens.SessionClaim), out var sessionId) ||
            !Guid.TryParse(bearer.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return AuthenticateResult.Fail("Invalid session.");

        var now = time.GetUtcNow();
        var valid = await (from session in db.AuthSessions.AsNoTracking()
                           join user in db.Users.AsNoTracking() on session.UserId equals user.Id
                           where session.Id == sessionId && user.Id == userId &&
                               session.RevokedAt == null && session.ExpiresAt > now &&
                               session.SecurityStamp == user.SecurityStamp &&
                               user.IsActive && user.PhoneNumberConfirmed &&
                               (user.LockoutEnd == null || user.LockoutEnd <= now)
                           select session.Id).AnyAsync(Context.RequestAborted);
        return valid
            ? AuthenticateResult.Success(new AuthenticationTicket(bearer.Principal, SessionTokens.Scheme))
            : AuthenticateResult.Fail("Invalid session.");
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }
}

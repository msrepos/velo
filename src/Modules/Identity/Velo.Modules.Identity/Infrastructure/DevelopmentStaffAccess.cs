using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure.Persistence;

namespace Velo.Modules.Identity.Infrastructure;

// Local operator tool only. No HTTP endpoint calls this service. Customer login and
// refresh never pass these permissions to SessionTokens.
public sealed class DevelopmentStaffAccess(IdentityDbContext db, SessionTokens tokens,
    IHostEnvironment environment, TimeProvider time, IOptions<AuthOptions> options)
{
    public async Task<DevelopmentAccessToken> IssueAsync(Guid userId,
        IReadOnlyCollection<string> permissions, CancellationToken ct)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Development staff tokens cannot be issued outside Development.");
        var now = time.GetUtcNow();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive && x.PhoneNumberConfirmed &&
            (x.LockoutEnd == null || x.LockoutEnd <= now), ct)
            ?? throw new InvalidOperationException("Select an active user with a verified phone number.");
        var session = new AuthSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, SecurityStamp = user.SecurityStamp!,
            RefreshTokenHash = "", CreatedAt = now,
            ExpiresAt = now.AddMinutes(options.Value.AccessTokenMinutes)
        };
        var response = tokens.Issue(user, session, permissions);
        db.AuthSessions.Add(session);
        await db.SaveChangesAsync(ct);
        // Do not expose its refresh token: this temporary management session cannot renew.
        return new(response.AccessToken, response.ExpiresIn, response.SessionId);
    }
}

public sealed record DevelopmentAccessToken(string AccessToken, long ExpiresIn, Guid SessionId);

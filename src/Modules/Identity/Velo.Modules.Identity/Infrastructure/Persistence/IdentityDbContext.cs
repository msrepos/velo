using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Velo.Modules.Identity.Domain;
using Velo.Modules.Identity.Infrastructure.Persistence.Models;

namespace Velo.Modules.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthThrottle> AuthThrottles => Set<AuthThrottle>();
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ApplicationUser>(user =>
        {
            user.ToTable("Users");
            // The sign-in flow must normalize to E.164 before saving a phone number.
            user.Property(x => x.PhoneNumber).HasMaxLength(16).IsRequired();
            user.Property(x => x.IsActive).HasDefaultValue(true);
            user.Property(x => x.Name).HasMaxLength(160);
            user.HasIndex(x => x.PhoneNumber).IsUnique().HasDatabaseName("PhoneNumberIndex");
        });

        builder.Entity<IdentityRole<Guid>>().ToTable("Roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<IdentityUserLogin<Guid>>(login =>
        {
            login.ToTable("UserLogins");
            login.Property(x => x.LoginProvider).HasMaxLength(128);
            login.Property(x => x.ProviderKey).HasMaxLength(128);
        });
        builder.Entity<IdentityUserToken<Guid>>(token =>
        {
            token.ToTable("UserTokens");
            token.Property(x => x.LoginProvider).HasMaxLength(128);
            token.Property(x => x.Name).HasMaxLength(128);
        });
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        builder.Entity<IdentityRole<Guid>>().HasData(new IdentityRole<Guid>
        {
            Id = Guid.Parse("2b0e772a-85bd-4830-b989-1674cc30d678"),
            Name = "Customer", NormalizedName = "CUSTOMER", ConcurrencyStamp = "velo-customer-role-v1"
        });

        builder.Entity<OtpChallenge>(challenge =>
        {
            challenge.ToTable("OtpChallenges");
            challenge.HasKey(x => x.Id);
            challenge.Property(x => x.PhoneNumber).HasMaxLength(16);
            challenge.Property(x => x.CodeHash).HasMaxLength(64).IsUnicode(false);
            challenge.Property(x => x.ClientKey).HasMaxLength(64).IsUnicode(false);
            challenge.Property(x => x.RegistrationName).HasMaxLength(160);
            challenge.Property(x => x.RegistrationEmail).HasMaxLength(256);
            challenge.HasIndex(x => new { x.PhoneNumber, x.CreatedAt });
            challenge.HasIndex(x => x.ExpiresAt);
        });

        builder.Entity<AuthSession>(session =>
        {
            session.ToTable("Sessions");
            session.HasKey(x => x.Id);
            session.Property(x => x.SecurityStamp).HasMaxLength(256);
            session.Property(x => x.RefreshTokenHash).HasMaxLength(64).IsUnicode(false);
            session.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            session.HasIndex(x => new { x.UserId, x.ExpiresAt });
        });

        builder.Entity<AuthThrottle>(throttle =>
        {
            throttle.ToTable("AuthThrottles");
            throttle.HasKey(x => x.Key);
            throttle.Property(x => x.Key).HasMaxLength(128).IsUnicode(false);
        });

        builder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}

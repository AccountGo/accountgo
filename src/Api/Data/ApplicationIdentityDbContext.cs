using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public class ApplicationIdentityDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options)
            : this((DbContextOptions)options)
        {
        }

        // Provider subclasses (Postgres) that carry their own migrations.
        protected ApplicationIdentityDbContext(DbContextOptions options)
            : base(options)
        {
        }

        public DbSet<RefreshTokenSession> RefreshTokens { get; set; } = null!;
        public DbSet<DataProtectionKeyEntity> DataProtectionKeys { get; set; } = null!;
        public DbSet<SecurityAuditEvent> SecurityAuditEvents { get; set; } = null!;
        public DbSet<UserRecoveryCode> UserRecoveryCodes { get; set; } = null!;
        public DbSet<TrustedDevice> TrustedDevices { get; set; } = null!;
        public DbSet<UserWebAuthnCredential> WebAuthnCredentials { get; set; } = null!;
        public DbSet<WebAuthnChallengeEntity> WebAuthnChallenges { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<RefreshTokenSession>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FamilyId).IsRequired();
                entity.Property(e => e.GraceReuseCount).IsRequired();
                entity.HasIndex(e => e.TokenHash);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.FamilyId);
            });

            builder.Entity<DataProtectionKeyEntity>(entity =>
            {
                entity.ToTable("DataProtectionKeys");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.FriendlyName);
            });

            builder.Entity<SecurityAuditEvent>(entity =>
            {
                entity.ToTable("SecurityAuditEvents");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.EventType);
                entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            });

            builder.Entity<UserRecoveryCode>(entity =>
            {
                entity.ToTable("UserRecoveryCodes");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CodeHash);
            });

            builder.Entity<TrustedDevice>(entity =>
            {
                entity.ToTable("TrustedDevices");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.TokenHash);
            });

            builder.Entity<UserWebAuthnCredential>(entity =>
            {
                entity.ToTable("UserWebAuthnCredentials");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.CredentialId).IsUnique();
            });

            builder.Entity<WebAuthnChallengeEntity>(entity =>
            {
                entity.ToTable("WebAuthnChallenges");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UserId).HasMaxLength(450).IsRequired();
                entity.Property(e => e.Purpose).HasMaxLength(50).IsRequired();
                entity.Property(e => e.JsonPayload).IsRequired();
                entity.HasIndex(e => new { e.UserId, e.Purpose }).IsUnique();
            });
        }
    }
}

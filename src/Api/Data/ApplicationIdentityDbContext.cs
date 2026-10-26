using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Service.D1Sync;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Api.Data
{
    public class ApplicationIdentityDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ID1SyncContext? _d1SyncContext;

        public ApplicationIdentityDbContext(DbContextOptions<ApplicationIdentityDbContext> options, ID1SyncContext? d1SyncContext = null)
            : base(options)
        {
            _d1SyncContext = d1SyncContext;
        }

        public DbSet<RefreshTokenSession> RefreshTokens { get; set; } = null!;
        public DbSet<DataProtectionKeyEntity> DataProtectionKeys { get; set; } = null!;
        public DbSet<SecurityAuditEvent> SecurityAuditEvents { get; set; } = null!;
        public DbSet<UserRecoveryCode> UserRecoveryCodes { get; set; } = null!;
        public DbSet<TrustedDevice> TrustedDevices { get; set; } = null!;
        public DbSet<UserWebAuthnCredential> WebAuthnCredentials { get; set; } = null!;
        public DbSet<WebAuthnChallengeEntity> WebAuthnChallenges { get; set; } = null!;

        // Every SaveChanges/SaveChangesAsync overload funnels into these two.
        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            if (_d1SyncContext == null)
                return base.SaveChanges(acceptAllChangesOnSuccess);

            // Capture states before saving: EF resets them to Unchanged/Detached afterwards.
            var pending = D1SqlGenerator.Capture(ChangeTracker);
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            pending.FlushTo(_d1SyncContext);
            return result;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (_d1SyncContext == null)
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

            // Capture states before saving: EF resets them to Unchanged/Detached afterwards.
            var pending = D1SqlGenerator.Capture(ChangeTracker);
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            pending.FlushTo(_d1SyncContext);
            return result;
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<RefreshTokenSession>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.TokenHash);
                entity.HasIndex(e => e.UserId);
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

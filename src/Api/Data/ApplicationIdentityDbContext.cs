using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Core.Domain;
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
        public DbSet<OAuthAuthorizationRequest> OAuthAuthorizationRequests { get; set; } = null!;
        public DbSet<DataProtectionKeyEntity> DataProtectionKeys { get; set; } = null!;
        public DbSet<SecurityAuditEvent> SecurityAuditEvents { get; set; } = null!;
        public DbSet<UserRecoveryCode> UserRecoveryCodes { get; set; } = null!;
        public DbSet<TrustedDevice> TrustedDevices { get; set; } = null!;
        public DbSet<UserWebAuthnCredential> WebAuthnCredentials { get; set; } = null!;
        public DbSet<WebAuthnChallengeEntity> WebAuthnChallenges { get; set; } = null!;
        public DbSet<ExternalTokenRedemption> ExternalTokenRedemptions { get; set; } = null!;

        private void AssignPublicIds()
        {
            foreach (var entry in ChangeTracker.Entries<IHasPublicId>())
            {
                if (entry.State != EntityState.Added)
                    continue;
                if (!string.IsNullOrWhiteSpace(entry.Entity.PublicId))
                    continue;
                var attr = entry.Metadata.ClrType.GetCustomAttribute<PublicIdPrefixAttribute>(inherit: false)
                    ?? throw new InvalidOperationException(
                        $"Entity '{entry.Metadata.ClrType.Name}' implements IHasPublicId without [PublicIdPrefix].");
                entry.Entity.PublicId = PublicIdGenerator.New(attr.Prefix);
            }
        }

        private void EnforcePublicIdImmutability()
        {
            foreach (var entry in ChangeTracker.Entries<IHasPublicId>())
            {
                if (entry.State != EntityState.Modified)
                    continue;
                var prop = entry.Property(nameof(IHasPublicId.PublicId));
                if (prop.IsModified && !string.Equals(prop.OriginalValue as string, prop.CurrentValue as string, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot modify PublicId of existing {entry.Metadata.ClrType.Name}.");
                }
            }
        }

        private void EnsurePublicIdsAssigned()
        {
            foreach (var entry in ChangeTracker.Entries<IHasPublicId>())
            {
                if (entry.State == EntityState.Added && string.IsNullOrWhiteSpace(entry.Entity.PublicId))
                {
                    throw new InvalidOperationException(
                        $"Added entity {entry.Metadata.ClrType.Name} has an empty PublicId.");
                }
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            AssignPublicIds();
            EnforceSecurityAuditEventImmutability();
            EnforcePublicIdImmutability();
            EnsurePublicIdsAssigned();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            AssignPublicIds();
            EnforceSecurityAuditEventImmutability();
            EnforcePublicIdImmutability();
            EnsurePublicIdsAssigned();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Tracked modify/delete is blocked in-process; the retention job uses <c>ExecuteDeleteAsync</c>,
        /// which bypasses change tracking and is checked by the database trigger instead.
        /// </summary>
        private void EnforceSecurityAuditEventImmutability()
        {
            var blocked = ChangeTracker.Entries<SecurityAuditEvent>()
                .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                .ToList();
            if (blocked.Count == 0) return;
            throw new InvalidOperationException(
                "SecurityAuditEvents rows are append-only evidence and cannot be modified or deleted.");
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<RefreshTokenSession>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.FamilyId).IsRequired();
                entity.Property(e => e.GraceReuseCount).IsRequired();
                entity.Property(e => e.Channel).IsRequired().HasMaxLength(32).HasDefaultValue(RefreshTokenChannels.Web);
                entity.Property(e => e.ClientId).HasMaxLength(64);
                entity.Property(e => e.DeviceLabel).HasMaxLength(200);
                entity.HasIndex(e => e.TokenHash);
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.FamilyId);
                entity.HasIndex(e => e.Channel);
            });

            builder.Entity<OAuthAuthorizationRequest>(entity =>
            {
                entity.ToTable("OAuthAuthorizationRequests");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasMaxLength(64);
                entity.Property(e => e.ClientId).IsRequired().HasMaxLength(64);
                entity.Property(e => e.RedirectUri).IsRequired().HasMaxLength(500);
                entity.Property(e => e.CodeChallenge).IsRequired().HasMaxLength(128);
                entity.Property(e => e.Scope).IsRequired().HasMaxLength(256);
                entity.Property(e => e.State).IsRequired().HasMaxLength(256);
                entity.Property(e => e.UserId).HasMaxLength(450);
                entity.Property(e => e.DeviceLabel).HasMaxLength(200);
                entity.Property(e => e.CodeHash).HasMaxLength(128);
                entity.Property(e => e.IssuedRefreshFamilyId).HasMaxLength(64);
                entity.HasIndex(e => e.CodeHash);
                entity.HasIndex(e => e.ExpiresAt);
                entity.HasIndex(e => e.ClientId);
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
                entity.Property(e => e.CorrelationId).HasMaxLength(128);
                entity.Property(e => e.Outcome).HasMaxLength(16);
                entity.HasIndex(e => e.EventType);
                entity.HasIndex(e => new { e.UserId, e.CreatedAt });
                entity.HasIndex(e => new { e.CompanyId, e.CreatedAt });
                entity.HasIndex(e => new { e.EventType, e.CreatedAt });
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

            builder.Entity<ExternalTokenRedemption>(entity =>
            {
                entity.ToTable("ExternalTokenRedemptions");
                entity.HasKey(e => new { e.Provider, e.TokenId });
                entity.Property(e => e.Provider).HasMaxLength(64).IsRequired();
                entity.Property(e => e.TokenId).HasMaxLength(128).IsRequired();
                entity.HasIndex(e => e.ExpiresAtUtc);
            });

            foreach (var entityType in builder.Model.GetEntityTypes())
            {
                if (!typeof(IHasPublicId).IsAssignableFrom(entityType.ClrType))
                    continue;
                builder.Entity(entityType.ClrType, entity =>
                {
                    entity.Property<string>(nameof(IHasPublicId.PublicId))
                        .HasMaxLength(PublicIdGenerator.MaxLength)
                        .IsRequired()
                        .HasDefaultValue(string.Empty);
                    entity.HasIndex(nameof(IHasPublicId.PublicId))
                        .IsUnique()
                        .HasFilter("[PublicId] <> ''");
                });
            }
        }
    }
}

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
        }
    }
}

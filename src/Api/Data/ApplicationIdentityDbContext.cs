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

        public override int SaveChanges()
        {
            if (_d1SyncContext == null)
                return base.SaveChanges();

            var pendingEntries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
                .ToList();

            var result = base.SaveChanges();

            foreach (var entry in pendingEntries)
            {
                try
                {
                    var sql = D1SqlGenerator.GenerateSql(entry);
                    if (!string.IsNullOrEmpty(sql))
                    {
                        _d1SyncContext.AddStatement(sql);
                    }
                }
                catch { }
            }

            return result;
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_d1SyncContext == null)
                return await base.SaveChangesAsync(cancellationToken);

            var pendingEntries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
                .ToList();

            var result = await base.SaveChangesAsync(cancellationToken);

            foreach (var entry in pendingEntries)
            {
                try
                {
                    var sql = D1SqlGenerator.GenerateSql(entry);
                    if (!string.IsNullOrEmpty(sql))
                    {
                        _d1SyncContext.AddStatement(sql);
                    }
                }
                catch { }
            }

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

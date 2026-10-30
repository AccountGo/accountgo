using Microsoft.EntityFrameworkCore;

namespace Api.Data.Postgres
{
    /// <summary>
    /// <see cref="ApplicationIdentityDbContext"/> on PostgreSQL; exists for its own migration set. Never syncs to D1.
    /// </summary>
    public sealed class PostgresIdentityDbContext : ApplicationIdentityDbContext
    {
        public PostgresIdentityDbContext(DbContextOptions<PostgresIdentityDbContext> options)
            : base(options, d1SyncContext: null)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            PostgresModelConventions.UseWallClockDateTimes(configurationBuilder);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            PostgresModelConventions.TranslateIndexFilters(builder);
        }
    }
}

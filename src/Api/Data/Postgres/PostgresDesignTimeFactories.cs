using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Data.Postgres
{
    // `dotnet ef migrations add <Name> --context PostgresApiDbContext --output-dir Data/Migrations/Postgres/ApiDb`
    // builds the model only; it never connects, so the connection string is a placeholder.
    internal static class PostgresDesignTime
    {
        public const string ConnectionString = "Host=localhost;Database=accountgo_design;Username=postgres";
    }

    public sealed class PostgresApiDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresApiDbContext>
    {
        public PostgresApiDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
                .UseNpgsql(PostgresDesignTime.ConnectionString)
                .Options;
            return new PostgresApiDbContext(options);
        }
    }

    public sealed class PostgresIdentityDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PostgresIdentityDbContext>
    {
        public PostgresIdentityDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PostgresIdentityDbContext>()
                .UseNpgsql(PostgresDesignTime.ConnectionString)
                .Options;
            return new PostgresIdentityDbContext(options);
        }
    }
}

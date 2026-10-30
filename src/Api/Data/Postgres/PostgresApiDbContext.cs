using System;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Api.Data.Postgres
{
    /// <summary>
    /// <see cref="ApiDbContext"/> on PostgreSQL. Same model; the subclass only exists so the Postgres migrations
    /// (Data/Migrations/Postgres) are found apart from the SQL Server ones.
    /// </summary>
    public sealed class PostgresApiDbContext : ApiDbContext
    {
        public PostgresApiDbContext(DbContextOptions<PostgresApiDbContext> options, ITenantContext? tenantContext = null)
            : base(options, tenantContext)
        {
        }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            PostgresModelConventions.UseWallClockDateTimes(configurationBuilder);
            PostgresModelConventions.UseNormalizedDecimals(configurationBuilder);
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            PostgresModelConventions.UseDefaultSchema(builder);
            PostgresModelConventions.TranslateIndexFilters(builder);

            // SQL Server rowversion ("timestamp"); nothing reads it and SQLite leaves it empty too.
            builder.Entity<Core.Domain.Financials.Account>()
                .Property(a => a.RowVersion)
                .HasColumnType("bytea");
        }
    }

    internal static class PostgresModelConventions
    {
        private static readonly Regex BracketedName = new(@"\[(\w+)\]", RegexOptions.Compiled);
        private static readonly Regex BoolEqualsOne = new(@"""(\w+)"" = 1\b", RegexOptions.Compiled);

        /// <summary>
        /// The domain mixes wall-clock dates (Kind Unspecified) with <c>DateTime.UtcNow</c>, and SQLite and SQL Server
        /// store both as they are. Npgsql refuses a UTC value for "timestamp without time zone", so drop the Kind on
        /// write; values read back are Unspecified, as they are from SQLite.
        /// </summary>
        public static void UseWallClockDateTimes(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<DateTime>()
                .HaveColumnType("timestamp without time zone")
                .HaveConversion<UnspecifiedKindConverter>();
            configurationBuilder.Properties<DateTime?>()
                .HaveColumnType("timestamp without time zone")
                .HaveConversion<UnspecifiedKindConverter>();
        }

        /// <summary>
        /// Postgres numeric returns the column's scale (4 becomes 4.0000), which then shows in messages and printed
        /// amounts. SQLite returns 4; strip trailing zeros on read to match.
        /// </summary>
        public static void UseNormalizedDecimals(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<decimal>().HaveConversion<NormalizedDecimalConverter>();
            configurationBuilder.Properties<decimal?>().HaveConversion<NormalizedDecimalConverter>();
        }

        private sealed class NormalizedDecimalConverter : ValueConverter<decimal, decimal>
        {
            // Dividing by 1 with 28 zero decimals drops trailing zeros without changing the value.
            public NormalizedDecimalConverter()
                : base(v => v, v => v / 1.0000000000000000000000000000m)
            {
            }
        }

        private sealed class UnspecifiedKindConverter : ValueConverter<DateTime, DateTime>
        {
            public UnspecifiedKindConverter()
                : base(v => DateTime.SpecifyKind(v, DateTimeKind.Unspecified), v => v)
            {
            }
        }

        /// <summary>
        /// Some entities map to SQL Server's "dbo" schema. SQLite/D1 have one namespace; keep Postgres the same so raw
        /// SQL and the D1 copy address every table by its bare name.
        /// </summary>
        public static void UseDefaultSchema(ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes())
            {
                if (entity.GetSchema() != null)
                    entity.SetSchema(null);
            }
        }

        /// <summary>
        /// The model writes index filters in SQL Server syntax (<c>[Col] = 1</c>). Postgres needs quoted names and
        /// boolean comparisons for bool columns.
        /// </summary>
        public static void TranslateIndexFilters(ModelBuilder builder)
        {
            foreach (var entity in builder.Model.GetEntityTypes())
            {
                foreach (var index in entity.GetIndexes().Where(i => i.GetFilter() != null))
                {
                    var filter = BracketedName.Replace(index.GetFilter()!, m => $"\"{m.Groups[1].Value}\"");
                    filter = BoolEqualsOne.Replace(filter, m =>
                    {
                        var property = entity.GetProperties().FirstOrDefault(p => p.GetColumnName() == m.Groups[1].Value);
                        var type = property == null ? null : System.Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                        return type == typeof(bool) ? $"\"{m.Groups[1].Value}\" = TRUE" : m.Value;
                    });
                    index.SetFilter(filter);
                }
            }
        }
    }
}

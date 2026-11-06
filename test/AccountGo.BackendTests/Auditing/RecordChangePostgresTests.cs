using Api.Auditing;
using Api.Data;
using Api.Data.Postgres;
using Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

public sealed class RecordChangePostgresTests
{
    [Fact]
    public void Postgres_migration_interceptor_and_append_only_trigger_work_when_configured()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var auditContext = new FixedAuditContext();
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
            .UseNpgsql(postgres.ConnectionString,
                npgsql => npgsql.ExecutionStrategy(dependencies => new BufferingExecutionStrategy(dependencies)))
            .AddInterceptors(new RecordChangeInterceptor(auditContext))
            .Options;
        using var db = new PostgresApiDbContext(options);
        db.Database.Migrate();

        var company = new Company { Name = "Record change integration", ShortName = Guid.NewGuid().ToString("N") };
        db.Companies.Add(company);
        db.SaveChanges();
        Assert.Contains(db.RecordChanges.IgnoreQueryFilters().AsNoTracking(), row =>
            row.EntityType == "Company" && row.EntityId == company.Id && row.CompanyId == company.Id);

        Assert.Throws<PostgresException>(() => db.Database.ExecuteSqlRaw(
            "UPDATE \"RecordChanges\" SET \"ActorKind\" = 'changed' WHERE \"EntityId\" = {0}", company.Id));
        Assert.Throws<PostgresException>(() => db.Database.ExecuteSqlRaw(
            "DELETE FROM \"RecordChanges\" WHERE \"EntityId\" = {0}", company.Id));
    }

    [Fact]
    public void Caller_owned_transaction_unique_index_failure_still_allows_caller_rollback()
    {
        if (!PostgresTestDatabase.IsConfigured) return;
        using var postgres = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>()
            .UseNpgsql(postgres.ConnectionString,
                npgsql => npgsql.ExecutionStrategy(dependencies => new BufferingExecutionStrategy(dependencies)))
            .AddInterceptors(new RecordChangeInterceptor(new FixedAuditContext()))
            .Options;
        using var db = new PostgresApiDbContext(options);
        db.Database.Migrate();

        var slug = "dup-" + Guid.NewGuid().ToString("N")[..10];
        db.Companies.Add(new Company { Name = "First", ShortName = "First", Slug = slug });
        db.SaveChanges();

        using var tx = db.Database.BeginTransaction();
        db.Companies.Add(new Company { Name = "Second", ShortName = "Second", Slug = slug });
        Assert.ThrowsAny<Exception>(() => db.SaveChanges());

        var rollbackError = Record.Exception(() => tx.Rollback());
        Assert.Null(rollbackError);
        Assert.Single(db.Companies.AsNoTracking().IgnoreQueryFilters().Where(c => c.Slug == slug));
    }

    private sealed class FixedAuditContext : IRequestAuditContext
    {
        public string? ActorUserId => "postgres-test-actor";
        public string ActorKind => "user";
        public string CorrelationId => "postgres-test-trace";
        public string? IpAddress => "127.0.0.1";
    }
}

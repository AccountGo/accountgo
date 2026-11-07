using System.Data.Common;
using System.Text.Json;
using Api.Auditing;
using Api.Data;
using Api.Middleware;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AccountGo.BackendTests.Auditing;

public sealed class RecordChangeInterceptorTests
{
    [Fact]
    public void Banking_transaction_and_reconciliation_lifecycle_are_audited()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using var db = CreateDb(connection, new FixedAuditContext());
        db.Database.EnsureCreated();
        var transaction = new BankTransaction { CompanyId = 1, BankId = 7, No = "BT-1", Amount = 25, Date = new DateTime(2026, 10, 1), Kind = BankTransactionKinds.MoneyOut };
        var reconciliation = new BankReconciliation { CompanyId = 1, BankId = 7, StatementEndDate = new DateTime(2026, 10, 31), Status = BankReconciliationStatuses.Draft, CreatedAt = DateTime.UtcNow };
        db.BankTransactions.Add(transaction);
        db.BankReconciliations.Add(reconciliation);
        db.SaveChanges();
        transaction.VoidGeneralLedgerHeaderId = 99;
        reconciliation.Status = BankReconciliationStatuses.Finished;
        reconciliation.FinishedBy = "actor-1";
        reconciliation.FinishedAt = DateTime.UtcNow;
        db.SaveChanges();
        reconciliation.Status = BankReconciliationStatuses.Undone;
        reconciliation.UndoneBy = "actor-1";
        reconciliation.UndoneAt = DateTime.UtcNow;
        reconciliation.UndoReason = "Statement correction";
        db.SaveChanges();

        var changes = db.RecordChanges.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).ToList();
        var txChanges = changes.Where(x => x.EntityType == "BankTransaction").ToList();
        Assert.Equal(2, txChanges.Count);
        Assert.Contains("VoidGeneralLedgerHeaderId", txChanges[1].ChangesJson);
        var reconChanges = changes.Where(x => x.EntityType == "BankReconciliation").ToList();
        Assert.Equal(3, reconChanges.Count);
        Assert.Contains("FinishedAt", reconChanges[1].ChangesJson);
        Assert.Contains("Statement correction", reconChanges[2].ChangesJson);
        using var finish = JsonDocument.Parse(reconChanges[1].ChangesJson);
        Assert.Equal(7, finish.RootElement.GetProperty("BankId").GetInt32());
        Assert.StartsWith("2026-10-31", finish.RootElement.GetProperty("StatementEndDate").GetString());
        Assert.All(changes, change => { Assert.Equal(1, change.CompanyId); Assert.Equal("actor-1", change.ActorUserId); Assert.NotEqual(default, change.OccurredAt); });
    }

    [Fact]
    public void Banking_audit_failure_rolls_back_reconciliation_finish()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using (var setup = CreateDb(connection, new FixedAuditContext()))
        {
            setup.Database.EnsureCreated();
            setup.BankReconciliations.Add(new BankReconciliation { CompanyId = 1, BankId = 7, StatementEndDate = new DateTime(2026, 10, 31), Status = BankReconciliationStatuses.Draft, CreatedAt = DateTime.UtcNow });
            setup.SaveChanges();
        }
        using var db = CreateDb(connection, new FixedAuditContext(), new FailRecordChangeInsert());
        var reconciliation = db.BankReconciliations.IgnoreQueryFilters().Single();
        reconciliation.Status = BankReconciliationStatuses.Finished;
        reconciliation.FinishedAt = DateTime.UtcNow;
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        using var verify = CreateDb(connection, new FixedAuditContext());
        Assert.Equal(BankReconciliationStatuses.Draft, verify.BankReconciliations.IgnoreQueryFilters().AsNoTracking().Single().Status);
        Assert.Single(verify.RecordChanges.IgnoreQueryFilters().AsNoTracking());
    }

    [Fact]
    public void Company_add_modify_delete_writes_tenant_scoped_append_only_snapshots()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        var audit = new FixedAuditContext();
        using var db = CreateDb(connection, audit);
        db.Database.EnsureCreated();
        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(connection);

        var company = new Company { Name = "Before", ShortName = "Test" };
        db.Companies.Add(company);
        db.SaveChanges();
        var added = Assert.Single(db.RecordChanges.IgnoreQueryFilters().AsNoTracking().ToList());
        Assert.Equal(company.Id, added.CompanyId);
        Assert.Equal(company.Id, added.EntityId);
        Assert.Equal("Company", added.EntityType);
        Assert.Equal("Added", added.Action);
        Assert.Equal("actor-1", added.ActorUserId);
        Assert.Equal("user", added.ActorKind);
        Assert.Equal("trace-1", added.CorrelationId);
        Assert.Equal("127.0.0.1", added.IpAddress);

        company.Name = "After";
        db.SaveChanges();
        var modified = db.RecordChanges.IgnoreQueryFilters().AsNoTracking().OrderBy(x => x.Id).Last();
        using var changes = JsonDocument.Parse(modified.ChangesJson);
        Assert.Equal("Before", changes.RootElement.GetProperty("Name")[0].GetString());
        Assert.Equal("After", changes.RootElement.GetProperty("Name")[1].GetString());
        Assert.Single(changes.RootElement.EnumerateObject());

        db.Companies.Remove(company);
        db.SaveChanges();
        Assert.Equal(new[] { "Added", "Modified", "Deleted" }, db.RecordChanges.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(x => x.Id).Select(x => x.Action).ToArray());
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("UPDATE RecordChanges SET ActorKind = 'changed'"));
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw("DELETE FROM RecordChanges"));
    }

    [Fact]
    public void Audit_insert_failure_rolls_back_business_insert()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using (var setup = CreateDb(connection, new FixedAuditContext())) setup.Database.EnsureCreated();
        var failure = new FailRecordChangeInsert();
        using var db = CreateDb(connection, new FixedAuditContext(), failure);
        db.Companies.Add(new Company { Name = "Must roll back", ShortName = "Rollback" });

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("Audit write rejected.", error.Message);
        Assert.True(failure.HadTransaction);
        Assert.True(failure.SharedTransaction);
        Assert.DoesNotContain(db.Companies.AsNoTracking().IgnoreQueryFilters(), c => c.ShortName == "Rollback");
        Assert.Empty(db.RecordChanges.IgnoreQueryFilters().AsNoTracking());
    }

    [Fact]
    public void Caller_owned_transaction_unique_index_failure_still_allows_caller_rollback()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using var db = CreateDb(connection, new FixedAuditContext());
        db.Database.EnsureCreated();
        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(connection);

        db.Companies.Add(new Company { Name = "First", ShortName = "First", Slug = "dup-slug" });
        db.SaveChanges();

        using var tx = db.Database.BeginTransaction();
        db.Companies.Add(new Company { Name = "Second", ShortName = "Second", Slug = "dup-slug" });
        var saveError = Assert.ThrowsAny<Exception>(() => db.SaveChanges());
        Assert.Contains("UNIQUE", saveError.Message + (saveError.InnerException?.Message ?? ""), StringComparison.OrdinalIgnoreCase);

        var rollbackError = Record.Exception(() => tx.Rollback());
        Assert.Null(rollbackError);
        Assert.Single(db.Companies.AsNoTracking().IgnoreQueryFilters().Where(c => c.Slug == "dup-slug"));
    }

    [Fact]
    public void Caller_owned_transaction_audit_insert_failure_reaches_caller_and_commits_nothing()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using (var setup = CreateDb(connection, new FixedAuditContext())) setup.Database.EnsureCreated();
        var failure = new FailRecordChangeInsert();
        using var db = CreateDb(connection, new FixedAuditContext(), failure);

        using var tx = db.Database.BeginTransaction();
        db.Companies.Add(new Company { Name = "Caller owned", ShortName = "caller-audit-fail" });
        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Equal("Audit write rejected.", error.Message);
        Assert.True(failure.HadTransaction);
        Assert.True(failure.SharedTransaction);

        var rollbackError = Record.Exception(() => tx.Rollback());
        Assert.Null(rollbackError);
        Assert.DoesNotContain(db.Companies.AsNoTracking().IgnoreQueryFilters(), c => c.ShortName == "caller-audit-fail");
        Assert.Empty(db.RecordChanges.IgnoreQueryFilters().AsNoTracking());
    }

    [Fact]
    public void Non_allowlisted_entity_is_not_recorded()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using var db = CreateDb(connection, new FixedAuditContext());
        db.Database.EnsureCreated();
        db.Logs.Add(new Log
        { CompanyId = 1, TimeStamp = DateTime.UtcNow, Level = "Info", Logger = "test", Message = "test", Username = "", CallSite = "", Thread = "", Exception = "", StackTrace = "" });
        db.SaveChanges();
        Assert.Empty(db.RecordChanges.IgnoreQueryFilters().AsNoTracking());
    }

    [Fact]
    public void Bank_account_numbers_are_masked_in_added_snapshots()
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
        connection.Open();
        using var db = CreateDb(connection, new FixedAuditContext());
        db.Database.EnsureCreated();
        db.Banks.Add(new Bank
        {
            CompanyId = 1, AccountId = 999, Name = "Primary", BankName = "Bank", CurrencyCode = "USD",
            Number = "123456789012", Iban = "PH001234567890123456789012", RoutingNumber = "987654321"
        });
        db.SaveChanges();

        var json = Assert.Single(db.RecordChanges.IgnoreQueryFilters().Where(x => x.EntityType == "Bank")).ChangesJson;
        Assert.Contains("****9012", json);
        Assert.Contains("****4321", json);
        Assert.DoesNotContain("123456789012", json);
        Assert.DoesNotContain("987654321", json);
        Assert.DoesNotContain("PH001234567890123456789012", json);
    }

    [Fact]
    public async Task Correlation_middleware_returns_trace_identifier_as_request_id()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-fallback-42";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);
        await context.Response.StartAsync();

        Assert.Equal("request-fallback-42", context.Items[CorrelationIdMiddleware.ItemKey]);
        Assert.Equal("request-fallback-42", context.Response.Headers[CorrelationIdMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData(Core.Authorization.ActorTrustDomain.StaffOperator, "staff")]
    [InlineData(Core.Authorization.ActorTrustDomain.ScopedService, "job")]
    [InlineData(Core.Authorization.ActorTrustDomain.ServicePrincipal, "job")]
    [InlineData(Core.Authorization.ActorTrustDomain.WorkspaceUser, "user")]
    [InlineData(Core.Authorization.ActorTrustDomain.Unauthenticated, "system")]
    public void Request_audit_context_resolves_actor_kind(Core.Authorization.ActorTrustDomain domain, string expected)
    {
        var tenant = new TenantContext();
        tenant.SetTenant(1, "workspace", CompanyMemberRole.Admin, Array.Empty<string>(), "actor", actorDomain: domain);
        var audit = new Api.Auditing.RequestAuditContext(new HttpContextAccessor(), tenant);

        Assert.Equal(expected, audit.ActorKind);
    }

    [Fact]
    public void Record_changes_query_filter_is_tenant_scoped()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        using (var initial = new ApiDbContext(options)) initial.Database.EnsureCreated();
        foreach (var companyId in new[] { 1, 2 })
        {
            var tenant = new TenantContext();
            tenant.SetTenant(companyId, "workspace", CompanyMemberRole.Admin, Array.Empty<string>(), "actor");
            using var writer = new ApiDbContext(options, tenant);
            writer.RecordChanges.Add(new RecordChange
            {
                CompanyId = companyId, EntityType = "Party", EntityId = companyId, Action = "Modified",
                ChangesJson = "{}", ActorKind = "user", CorrelationId = "trace", OccurredAt = DateTime.UtcNow
            });
            writer.SaveChanges();
        }
        var readerTenant = new TenantContext();
        readerTenant.SetTenant(2, "workspace", CompanyMemberRole.Admin, Array.Empty<string>(), "actor");
        using var reader = new ApiDbContext(options, readerTenant);

        var row = Assert.Single(reader.RecordChanges.AsNoTracking());
        Assert.Equal(2, row.CompanyId);
    }

    [Fact]
    public void Every_audited_type_has_one_integer_primary_key()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        using var db = CreateDb(connection, new FixedAuditContext());

        foreach (var type in RecordChangeInterceptor.AllowList.Keys)
        {
            var key = Assert.IsAssignableFrom<Microsoft.EntityFrameworkCore.Metadata.IKey>(db.Model.FindEntityType(type)?.FindPrimaryKey());
            var property = Assert.Single(key.Properties);
            Assert.Equal(typeof(int), Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType);
        }
    }

    private static ApiDbContext CreateDb(SqliteConnection connection, IRequestAuditContext audit, params IInterceptor[] additional)
    {
        var interceptor = new IInterceptor[] { new RecordChangeInterceptor(audit) }.Concat(additional).ToArray();
        return new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection)
            .AddInterceptors(interceptor).Options);
    }

    private sealed class FixedAuditContext : IRequestAuditContext
    {
        public string? ActorUserId => "actor-1";
        public string ActorKind => "user";
        public string CorrelationId => "trace-1";
        public string? IpAddress => "127.0.0.1";
    }

    private sealed class FailRecordChangeInsert : DbCommandInterceptor
    {
        public bool HadTransaction { get; private set; }
        public bool SharedTransaction { get; private set; }
        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            if (command.CommandText.Contains("RecordChanges", StringComparison.Ordinal))
            {
                HadTransaction = command.Transaction is not null;
                SharedTransaction = eventData.Context?.Database.CurrentTransaction?.GetDbTransaction() == command.Transaction;
                throw new InvalidOperationException("Audit write rejected.");
            }
            return result;
        }
    }
}

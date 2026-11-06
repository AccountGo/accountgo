// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using System.Linq;
using Api.Data;
using Api.Data.Postgres;
using Api.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class PostgresAuditFactAttribute : FactAttribute
{
    public PostgresAuditFactAttribute()
    {
        if (!PostgresTestDatabase.IsConfigured) Skip = "Postgres test connection is not configured.";
    }
}

public sealed class AuditEvidenceIntegrityMigrationTests
{
    [Fact]
    public void SqlServerMigration_DoesNotEmitSqliteStatements()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlServer("Server=localhost;Database=unused;Integrated Security=true;TrustServerCertificate=true").Options;
        using var db = new ApiDbContext(options, new TenantContext());
        var script = db.GetService<IMigrator>().GenerateScript("20261105163932_LinkedEntryPairs", "20261106120000_AuditEvidenceIntegrity");
        Assert.DoesNotContain("TR_AuditLog_Evidence_Update", script);
        Assert.DoesNotContain("substr(OLD", script);
    }

    [Fact]
    public void SqliteMigration_EmitsEvidenceTriggersAndUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite("Data Source=:memory:").Options;
        using var db = new ApiDbContext(options, new TenantContext());
        var script = db.GetService<IMigrator>().GenerateScript("20261105163932_LinkedEntryPairs", "20261106120000_AuditEvidenceIntegrity");
        Assert.Contains("TR_AuditLog_Evidence_Update", script);
        Assert.Contains("TR_AuditLog_Evidence_Delete", script);
        Assert.Contains("IX_AuditLog_ApprovalState", script);
    }

    [PostgresAuditFact]
    public void PostgresMigration_PreservesLegacyApprovalsAndBlocksEvidenceChanges()
    {
        using var database = PostgresTestDatabase.CreateIfConfigured()!;
        var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(database.ConnectionString).Options;
        using var db = new PostgresApiDbContext(options, new TenantContext());
        db.GetService<IMigrator>().Migrate("20261105163943_LinkedEntryPairs");
        db.Database.ExecuteSqlRaw("""
            INSERT INTO "AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
            VALUES (1, 'user', TIMESTAMP '2026-01-01 00:00:00', 1, '__AccessDelegationApproval', 'old-consumed', 'Status', 'Consumed', '{{}}'),
                   (1, 'user', TIMESTAMP '2026-01-01 00:00:00', 1, '__AccessDelegationApproval', 'old-revoked', 'Status', 'Revoked', '{{}}')
            """);
        db.Database.Migrate();
        Assert.Equal(2, db.AuditLogs.IgnoreQueryFilters().Count(a => a.TableName == "__AccessDelegationApproval" && (a.OriginalValue == "Consumed" || a.OriginalValue == "Revoked")));
        Assert.Equal(2, db.AuditLogs.IgnoreQueryFilters().Count(a => a.TableName == "__AccessDelegationApprovalState" && (a.OriginalValue == "Consumed" || a.OriginalValue == "Revoked")));
        Assert.Throws<PostgresException>(() => db.Database.ExecuteSqlRaw("UPDATE \"AuditLog\" SET \"NewValue\" = 'tampered' WHERE \"RecordId\" = 'old-consumed'"));
        Assert.Throws<PostgresException>(() => db.Database.ExecuteSqlRaw("DELETE FROM \"AuditLog\" WHERE \"RecordId\" = 'old-revoked'"));
        Assert.Throws<PostgresException>(() => db.Database.ExecuteSqlRaw("""
            INSERT INTO "AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
            VALUES (1, 'user', TIMESTAMP '2026-01-01 00:00:00', 1, '__AccessDelegationApprovalState', 'old-consumed', 'Status', 'Consumed', '{{}}')
            """));
    }
}

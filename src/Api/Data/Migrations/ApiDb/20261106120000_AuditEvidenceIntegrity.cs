// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Schema;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.ApiDb;

[DbContext(typeof(ApiDbContext))]
[Migration("20261106120000_AuditEvidenceIntegrity")]
public sealed class AuditEvidenceIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite") return;
        migrationBuilder.Sql(SqliteAuditEvidenceSql.Backfill);
        migrationBuilder.Sql(SqliteAuditEvidenceSql.Index);
        migrationBuilder.Sql(SqliteAuditEvidenceSql.UpdateTrigger);
        migrationBuilder.Sql(SqliteAuditEvidenceSql.DeleteTrigger);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (ActiveProvider != "Microsoft.EntityFrameworkCore.Sqlite") return;
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_AuditLog_Evidence_Update\"");
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_AuditLog_Evidence_Delete\"");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AuditLog_ApprovalState\"");
    }
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data.Postgres;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Api.Data.Migrations.Postgres.ApiDb;

[DbContext(typeof(PostgresApiDbContext))]
[Migration("20261106120001_AuditEvidenceIntegrity")]
public sealed class AuditEvidenceIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO "AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
            SELECT a."CompanyId", a."UserName", a."AuditEventDateUTC", 1, '__AccessDelegationApprovalState', a."RecordId", 'Status', a."OriginalValue", a."NewValue"
            FROM "AuditLog" AS a
            WHERE a."TableName" = '__AccessDelegationApproval' AND a."OriginalValue" IN ('Consumed', 'Revoked')
              AND NOT EXISTS (SELECT 1 FROM "AuditLog" AS s WHERE s."CompanyId" = a."CompanyId" AND s."TableName" = '__AccessDelegationApprovalState' AND s."RecordId" = a."RecordId")
            """);
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_AuditLog_ApprovalState" ON "AuditLog" ("CompanyId", "TableName", "RecordId") WHERE "TableName" = '__AccessDelegationApprovalState'
            """);
        migrationBuilder.Sql("""
            CREATE FUNCTION audit_log_evidence_immutable() RETURNS trigger AS $$
            BEGIN
                IF left(OLD."TableName", 2) = '__' THEN
                    RAISE EXCEPTION 'Audit evidence is append-only';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql
            """);
        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_AuditLog_Evidence" BEFORE UPDATE OR DELETE ON "AuditLog"
            FOR EACH ROW EXECUTE FUNCTION audit_log_evidence_immutable()
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TRIGGER IF EXISTS \"TR_AuditLog_Evidence\" ON \"AuditLog\"");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_log_evidence_immutable()");
        migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_AuditLog_ApprovalState\"");
    }
}

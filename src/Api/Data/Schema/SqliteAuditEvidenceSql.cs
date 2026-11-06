// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Api.Data.Schema;

internal static class SqliteAuditEvidenceSql
{
    internal const string Backfill = """
        INSERT INTO "AuditLog" ("CompanyId", "UserName", "AuditEventDateUTC", "AuditEventType", "TableName", "RecordId", "FieldName", "OriginalValue", "NewValue")
        SELECT a."CompanyId", a."UserName", a."AuditEventDateUTC", 1, '__AccessDelegationApprovalState', a."RecordId", 'Status', a."OriginalValue", a."NewValue"
        FROM "AuditLog" AS a
        WHERE a."TableName" = '__AccessDelegationApproval' AND a."OriginalValue" IN ('Consumed', 'Revoked')
          AND NOT EXISTS (SELECT 1 FROM "AuditLog" AS s WHERE s."CompanyId" = a."CompanyId" AND s."TableName" = '__AccessDelegationApprovalState' AND s."RecordId" = a."RecordId")
        """;
    internal const string Index = """
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_AuditLog_ApprovalState" ON "AuditLog" ("CompanyId", "TableName", "RecordId") WHERE "TableName" = '__AccessDelegationApprovalState'
        """;
    internal const string UpdateTrigger = """
        CREATE TRIGGER IF NOT EXISTS "TR_AuditLog_Evidence_Update" BEFORE UPDATE ON "AuditLog"
        WHEN substr(OLD."TableName", 1, 2) = '__'
        BEGIN SELECT RAISE(ABORT, 'Audit evidence is append-only'); END
        """;
    internal const string DeleteTrigger = """
        CREATE TRIGGER IF NOT EXISTS "TR_AuditLog_Evidence_Delete" BEFORE DELETE ON "AuditLog"
        WHEN substr(OLD."TableName", 1, 2) = '__'
        BEGIN SELECT RAISE(ABORT, 'Audit evidence is append-only'); END
        """;
}

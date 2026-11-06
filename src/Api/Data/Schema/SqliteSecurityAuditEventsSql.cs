// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

namespace Api.Data.Schema;

/// <summary>
/// Append-only + retention-floor triggers for <c>SecurityAuditEvents</c> on SQLite.
/// Floors are fixed in SQL so retention cannot be shortened without a new migration.
/// </summary>
public static class SqliteSecurityAuditEventsSql
{
    public const string UpdateTrigger = """
        CREATE TRIGGER IF NOT EXISTS "TR_SecurityAuditEvents_Immutable_Update"
        BEFORE UPDATE ON "SecurityAuditEvents"
        BEGIN
            SELECT RAISE(ABORT, 'SecurityAuditEvents rows cannot be updated');
        END;
        """;

    /// <summary>
    /// DELETE is allowed only when <c>CreatedAt</c> is strictly older than the floor
    /// (365 days for <c>data.*</c> / <c>invoice.pdf.public</c>, otherwise 730 days).
    /// Compares EF's SQLite TEXT timestamps with <c>datetime('now', …)</c> (UTC).
    /// </summary>
    public const string DeleteTrigger = """
        CREATE TRIGGER IF NOT EXISTS "TR_SecurityAuditEvents_Retention_Delete"
        BEFORE DELETE ON "SecurityAuditEvents"
        BEGIN
            SELECT RAISE(ABORT, 'SecurityAuditEvents cannot be deleted before the retention floor')
            WHERE NOT (
                CASE
                    WHEN OLD."EventType" LIKE 'data.%' OR OLD."EventType" = 'invoice.pdf.public'
                        THEN julianday(OLD."CreatedAt") < julianday('now', '-365 days')
                    ELSE julianday(OLD."CreatedAt") < julianday('now', '-730 days')
                END
            );
        END;
        """;

    public const string DropUpdateTrigger = """DROP TRIGGER IF EXISTS "TR_SecurityAuditEvents_Immutable_Update";""";
    public const string DropDeleteTrigger = """DROP TRIGGER IF EXISTS "TR_SecurityAuditEvents_Retention_Delete";""";
}

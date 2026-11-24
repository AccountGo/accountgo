using Api.Data;
using Api.Data.Schema;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Migrations;

public sealed class DocumentCaptureSqliteSchemaTests
{
    [Fact]
    public void Upgrade_RecreatesCaptureTablesIdempotentlyWithLowCompoundSelectLimit()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options))
        {
            db.Database.EnsureCreated();
        }

        Execute(connection, "DROP TABLE \"PartyAliases\"; DROP TABLE \"DocumentCaptureAiMappings\"; DROP TABLE \"DocumentCaptureLinks\"; DROP TABLE \"DocumentCaptureAttempts\"; DROP TABLE \"DocumentCaptureBlobs\"; DROP TABLE \"DocumentCaptures\";");
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        SqliteSchemaUpgrader.Upgrade(connection);
        SqliteSchemaUpgrader.Upgrade(connection);

        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptures';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureBlobs';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureAttempts';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureLinks';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'PartyAliases';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureAiMappings';"));
        Assert.Contains("UNIQUE", ScalarText(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureAiMappings_CompanyId_CaptureId_ExtractionGeneration';"), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureAiMappings_Cache';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureAiMappings_Status_LeaseUntil';"));
        Assert.Contains("UNIQUE", ScalarText(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_PartyAliases_CompanyId_Role_NormalizedAlias';"), StringComparison.OrdinalIgnoreCase);
        var captureRetentionIndex = ScalarText(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptures_ExpiresAt_Unaccepted';");
        Assert.Contains("ON \"DocumentCaptures\" (\"ExpiresAt\")", captureRetentionIndex, StringComparison.Ordinal);
        Assert.Contains("\"AcceptedAt\" IS NULL AND \"RejectedAt\" IS NULL AND \"BytesDeletedAt\" IS NULL", captureRetentionIndex, StringComparison.Ordinal);
        Assert.Contains("ON \"DocumentCaptureAttempts\" (\"ReservedAt\")", ScalarText(connection,
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptureAttempts_ReservedAt';"), StringComparison.Ordinal);
        Assert.Equal(0, ScalarInt(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'IX_DocumentCaptures_CompanyId_Status_ExpiresAt';"));
        Assert.All(new[] { "DocumentCaptures", "DocumentCaptureBlobs", "DocumentCaptureAttempts", "DocumentCaptureLinks", "DocumentCaptureAiMappings" }, table =>
            Assert.DoesNotContain(table, SqliteSchemaUpgrader.TenantTables));
        Assert.Equal(1, ScalarInt(connection,
            "SELECT COUNT(*) FROM pragma_table_info('DocumentCaptureLinks') WHERE name = 'TaxTreatment' AND \"notnull\" = 0;"));
    }

    [Fact]
    public void Upgrade_AddsTheNullableTaxTreatmentColumnToExistingCaptureLinksAndKeepsTheirRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options))
        {
            db.Database.EnsureCreated();
        }

        // A database from before the column existed: a links table without it, holding one row.
        Execute(connection, "PRAGMA foreign_keys = OFF; ALTER TABLE \"DocumentCaptureLinks\" DROP COLUMN \"TaxTreatment\";");
        Execute(connection, "INSERT INTO \"DocumentCaptureLinks\" (\"CompanyId\", \"CaptureId\", \"TargetType\", \"TargetId\", \"LinkedBy\", \"LinkedAt\") VALUES (1, 1, 'bill', 1, 'tester', '2026-11-20T00:00:00');");
        Assert.Equal(0, ScalarInt(connection, "SELECT COUNT(*) FROM pragma_table_info('DocumentCaptureLinks') WHERE name = 'TaxTreatment';"));

        SqliteSchemaUpgrader.Upgrade(connection);
        SqliteSchemaUpgrader.Upgrade(connection);

        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM pragma_table_info('DocumentCaptureLinks') WHERE name = 'TaxTreatment';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM \"DocumentCaptureLinks\" WHERE \"TaxTreatment\" IS NULL;"));
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar()) ?? string.Empty;
    }
}

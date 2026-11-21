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

        Execute(connection, "DROP TABLE \"DocumentCaptureLinks\"; DROP TABLE \"DocumentCaptureAttempts\"; DROP TABLE \"DocumentCaptureBlobs\"; DROP TABLE \"DocumentCaptures\";");
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        SqliteSchemaUpgrader.Upgrade(connection);
        SqliteSchemaUpgrader.Upgrade(connection);

        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptures';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureBlobs';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureAttempts';"));
        Assert.Equal(1, ScalarInt(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'DocumentCaptureLinks';"));
        Assert.All(new[] { "DocumentCaptures", "DocumentCaptureBlobs", "DocumentCaptureAttempts", "DocumentCaptureLinks" }, table =>
            Assert.DoesNotContain(table, SqliteSchemaUpgrader.TenantTables));
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
}

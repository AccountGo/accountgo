// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

using Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public sealed class StatementOwnerMovementMigrationTests
{
    [Fact]
    public void ExistingDescriptionRules_BecomeExpenseRulesOnSqliteUpgrade()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE BankStatementDescriptionRule (Id INTEGER PRIMARY KEY, DescriptionKey TEXT); INSERT INTO BankStatementDescriptionRule VALUES (1, 'old expense');";
        command.ExecuteNonQuery();
        using var db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options);
        var migration = new Api.Data.Migrations.ApiDb.StatementOwnerMovementRules();
        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        foreach (var migrationCommand in commands)
        {
            command.CommandText = migrationCommand.CommandText;
            command.ExecuteNonQuery();
        }
        command.CommandText = "SELECT Action FROM BankStatementDescriptionRule WHERE Id = 1";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void SqliteSchemaUpgrader_AddsActionToExistingDescriptionRules()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        using (var db = new ApiDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE \"__SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL);");
            db.Database.ExecuteSqlRaw("INSERT INTO \"__SchemaVersion\" (\"Version\", \"AppliedAt\") VALUES (1, 'legacy');");
            db.Database.ExecuteSqlRaw("ALTER TABLE \"BankStatementDescriptionRule\" DROP COLUMN \"Action\";");
            db.Database.ExecuteSqlRaw("INSERT INTO \"BankStatementDescriptionRule\" (\"CompanyId\", \"DescriptionKey\", \"CreatedAt\", \"LastUsedAt\") VALUES (1, 'legacy description', '2026-01-01', '2026-01-01');");
        }

        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Action\" FROM \"BankStatementDescriptionRule\" WHERE \"DescriptionKey\" = 'legacy description';";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void SqliteSchemaUpgrader_AddsPartnerReferencesToExistingTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
        using (var db = new ApiDbContext(options))
        {
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw("CREATE TABLE \"__SchemaVersion\" (\"Version\" INTEGER NOT NULL PRIMARY KEY, \"AppliedAt\" TEXT NOT NULL);");
            db.Database.ExecuteSqlRaw("INSERT INTO \"__SchemaVersion\" (\"Version\", \"AppliedAt\") VALUES (1, 'legacy');");
            db.Database.ExecuteSqlRaw("ALTER TABLE \"CompanySetting\" DROP COLUMN \"PartnerSetupJournalEntryId\";");
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=OFF;");
            db.Database.ExecuteSqlRaw(@"
CREATE TABLE ""BankStatementDescriptionRule_legacy"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""CompanyId"" INTEGER NOT NULL,
  ""DescriptionKey"" TEXT NOT NULL,
  ""Action"" INTEGER NOT NULL DEFAULT 0,
  ""VendorId"" INTEGER NULL,
  ""ExpenseAccountId"" INTEGER NULL,
  ""CreatedAt"" TEXT NOT NULL,
  ""LastUsedAt"" TEXT NOT NULL
);
INSERT INTO ""BankStatementDescriptionRule_legacy"" SELECT ""Id"", ""CompanyId"", ""DescriptionKey"", ""Action"", ""VendorId"", ""ExpenseAccountId"", ""CreatedAt"", ""LastUsedAt"" FROM ""BankStatementDescriptionRule"";
DROP TABLE ""BankStatementDescriptionRule"";
ALTER TABLE ""BankStatementDescriptionRule_legacy"" RENAME TO ""BankStatementDescriptionRule"";");
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
        }

        Api.Data.Schema.SqliteSchemaUpgrader.Upgrade(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BankStatementDescriptionRule') WHERE name = 'PartnerId';";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CompanySetting') WHERE name = 'PartnerSetupJournalEntryId';";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}

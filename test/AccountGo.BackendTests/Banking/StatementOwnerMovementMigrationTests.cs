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
}

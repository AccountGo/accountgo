// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankingPermissionsMigrationTests
{
    [Fact]
    public void D1_0033_RunsUnderCompoundSelectLimit_AndMigratesCashBanksGrants()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        Execute(connection, @"
CREATE TABLE ""SecurityGroup"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL, ""DisplayName"" TEXT);
CREATE TABLE ""SecurityPermission"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL, ""DisplayName"" TEXT, ""SecurityGroupId"" INTEGER NOT NULL);
CREATE TABLE ""SecurityRole"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""Name"" TEXT NOT NULL);
CREATE TABLE ""SecurityRolePermission"" (""SecurityRoleId"" INTEGER NOT NULL, ""SecurityPermissionId"" INTEGER NOT NULL);
INSERT INTO ""SecurityGroup"" (""Name"", ""DisplayName"") VALUES ('Financials', 'General Ledger & Banking');
INSERT INTO ""SecurityPermission"" (""Name"", ""DisplayName"", ""SecurityGroupId"") VALUES ('financials.cashbanks.view', 'View Cash & Banks', 1);
INSERT INTO ""SecurityPermission"" (""Name"", ""DisplayName"", ""SecurityGroupId"") VALUES ('financials.cashbanks.manage', 'Manage Cash & Banks', 1);
INSERT INTO ""SecurityRole"" (""Name"") VALUES ('Accountant');
INSERT INTO ""SecurityRole"" (""Name"") VALUES ('Auditor / Viewer');
INSERT INTO ""SecurityRolePermission"" (""SecurityRoleId"", ""SecurityPermissionId"") VALUES (1, 1);
INSERT INTO ""SecurityRolePermission"" (""SecurityRoleId"", ""SecurityPermissionId"") VALUES (1, 2);
INSERT INTO ""SecurityRolePermission"" (""SecurityRoleId"", ""SecurityPermissionId"") VALUES (2, 1);
");

        var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0033_banking_permissions.sql"));
        var ex = Record.Exception(() => ExecuteScript(connection, migration));
        Assert.Null(ex);

        Assert.Equal(0, ScalarLong(connection, @"SELECT COUNT(*) FROM ""SecurityPermission"" WHERE ""Name"" LIKE 'financials.cashbanks.%';"));
        Assert.Equal(5, ScalarLong(connection, @"SELECT COUNT(*) FROM ""SecurityPermission"" WHERE ""Name"" LIKE 'banking.%';"));
        Assert.Equal(1, ScalarLong(connection, @"SELECT COUNT(*) FROM ""SecurityGroup"" WHERE ""Name"" = 'Banking';"));
        Assert.True(ScalarLong(connection, @"
SELECT COUNT(*) FROM ""SecurityRolePermission"" rp
JOIN ""SecurityPermission"" p ON p.""Id"" = rp.""SecurityPermissionId""
JOIN ""SecurityRole"" r ON r.""Id"" = rp.""SecurityRoleId""
WHERE r.""Name"" = 'Accountant' AND p.""Name"" = 'banking.accounts.manage';") >= 1);
        Assert.Equal(0, ScalarLong(connection, @"
SELECT COUNT(*) FROM ""SecurityRolePermission"" rp
JOIN ""SecurityPermission"" p ON p.""Id"" = rp.""SecurityPermissionId""
JOIN ""SecurityRole"" r ON r.""Id"" = rp.""SecurityRoleId""
WHERE r.""Name"" = 'Accountant' AND p.""Name"" = 'banking.reconcile.undo';"));
    }

    private static void ExecuteScript(SqliteConnection connection, string script)
    {
        var statement = new StringBuilder();
        foreach (var line in script.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal) || trimmed.Length == 0)
                continue;
            statement.AppendLine(line);
            if (!trimmed.EndsWith(';'))
                continue;
            Execute(connection, statement.ToString());
            statement.Clear();
        }
        if (statement.Length > 0)
            Execute(connection, statement.ToString());
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AccountGo.sln"))
                || File.Exists(Path.Combine(dir.FullName, "src", "Api", "Api.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Data.Common;
using Api.Data.Schema;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AccountGo.BackendTests.Security;

public class RefreshTokenFamilyBackfillTests
{
    [Fact]
    public void SqliteSchemaUpgrader_BackfillsFamilyId_AndGraceReuseCount_UnderCompoundSelectLimit()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        // Match D1's tight compound-select limit used by other schema backfill tests.
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE ""RefreshTokens"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""UserId"" TEXT NOT NULL,
  ""TokenHash"" TEXT NOT NULL,
  ""ExpiresAt"" TEXT NOT NULL,
  ""CreatedAt"" TEXT NOT NULL,
  ""CreatedByIp"" TEXT NULL,
  ""RevokedAt"" TEXT NULL,
  ""ReplacedByTokenHash"" TEXT NULL,
  ""UserAgent"" TEXT NULL
);
INSERT INTO ""RefreshTokens"" (""UserId"", ""TokenHash"", ""ExpiresAt"", ""CreatedAt"")
VALUES ('u1', 'hash-a', '2099-01-01', '2026-01-01');
INSERT INTO ""RefreshTokens"" (""UserId"", ""TokenHash"", ""ExpiresAt"", ""CreatedAt"")
VALUES ('u1', 'hash-b', '2099-01-01', '2026-01-01');
";
            cmd.ExecuteNonQuery();
        }

        // Same ALTER path as SqliteSchemaUpgrader.EnsureLifecycleColumns (D1 0031).
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
ALTER TABLE ""RefreshTokens"" ADD COLUMN ""FamilyId"" TEXT NOT NULL DEFAULT '';
ALTER TABLE ""RefreshTokens"" ADD COLUMN ""GraceReuseCount"" INTEGER NOT NULL DEFAULT 0;
";
            cmd.ExecuteNonQuery();
        }

        var ex = Record.Exception(() => SqliteSchemaUpgrader.BackfillRefreshTokenFamilyIds(connection));
        Assert.Null(ex);

        Assert.True(ColumnExists(connection, "FamilyId"));
        Assert.True(ColumnExists(connection, "GraceReuseCount"));

        Assert.Equal("1", ReadFamilyId(connection, "hash-a"));
        Assert.Equal("2", ReadFamilyId(connection, "hash-b"));
        Assert.Equal(0, ReadGraceReuseCount(connection, "hash-a"));
        Assert.Equal(0, ReadGraceReuseCount(connection, "hash-b"));
    }

    [Fact]
    public void BackfillRefreshTokenFamilyIds_IsIdempotent_OneStatement()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE ""RefreshTokens"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""UserId"" TEXT NOT NULL,
  ""FamilyId"" TEXT NOT NULL DEFAULT '',
  ""GraceReuseCount"" INTEGER NOT NULL DEFAULT 0,
  ""TokenHash"" TEXT NOT NULL,
  ""ExpiresAt"" TEXT NOT NULL,
  ""CreatedAt"" TEXT NOT NULL
);
INSERT INTO ""RefreshTokens"" (""UserId"", ""FamilyId"", ""TokenHash"", ""ExpiresAt"", ""CreatedAt"")
VALUES ('u1', '', 'h1', '2099-01-01', '2026-01-01');
INSERT INTO ""RefreshTokens"" (""UserId"", ""FamilyId"", ""TokenHash"", ""ExpiresAt"", ""CreatedAt"")
VALUES ('u1', 'already', 'h2', '2099-01-01', '2026-01-01');
";
            cmd.ExecuteNonQuery();
        }

        SqliteSchemaUpgrader.BackfillRefreshTokenFamilyIds(connection);
        SqliteSchemaUpgrader.BackfillRefreshTokenFamilyIds(connection);

        Assert.Equal("1", ReadFamilyId(connection, "h1"));
        Assert.Equal("already", ReadFamilyId(connection, "h2"));
    }

    private static bool ColumnExists(DbConnection connection, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT 1 FROM pragma_table_info('RefreshTokens') WHERE name = $c LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$c";
        p.Value = column;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static string ReadFamilyId(DbConnection connection, string tokenHash)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT ""FamilyId"" FROM ""RefreshTokens"" WHERE ""TokenHash"" = $h;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$h";
        p.Value = tokenHash;
        cmd.Parameters.Add(p);
        return Assert.IsType<string>(cmd.ExecuteScalar());
    }

    private static int ReadGraceReuseCount(DbConnection connection, string tokenHash)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"SELECT ""GraceReuseCount"" FROM ""RefreshTokens"" WHERE ""TokenHash"" = $h;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$h";
        p.Value = tokenHash;
        cmd.Parameters.Add(p);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Text;
using Api.Data;
using Api.Data.Schema;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankSchemaCleanupTests
{
    [Fact]
    public void D1_0032_RunsUnderCompoundSelectLimit()
    {
        using var connection = OpenLegacyBankDb();
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        // Preflight-clean: every bank already has a dedicated postable cash account.
        SeedEligibleBanks(connection);

        var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0032_bank_accounts.sql"));
        var ex = Record.Exception(() => ExecuteScript(connection, migration));
        Assert.Null(ex);

        Assert.True(ColumnExists(connection, "Bank", "IsDefaultForReceipts"));
        Assert.True(ColumnExists(connection, "Bank", "ArchivedAt"));
        Assert.True(IndexExists(connection, "UX_Bank_Company_Account"));
        Assert.True(TriggerExists(connection, "trg_Bank_AccountId_required_insert"));
    }

    [Fact]
    public void D1_0032_AbortsWhenAccountIdStillNull()
    {
        using var connection = OpenLegacyBankDb();
        SQLitePCL.raw.sqlite3_limit(connection.Handle, SQLitePCL.raw.SQLITE_LIMIT_COMPOUND_SELECT, 1);

        Execute(connection, @"
INSERT INTO ""Account"" (""Id"", ""AccountClassId"", ""ParentAccountId"", ""DrOrCrSide"", ""CompanyId"", ""AccountCode"", ""AccountName"", ""IsCash"", ""IsContraAccount"")
VALUES (5, 1, 2, 1, 1, '10110', 'Cash in Hand and in Bank', 1, 0);
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES (1, 1, 1, 'Orphan', NULL, 0, 1);
");

        var migration = File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0032_bank_accounts.sql"));
        var ex = Assert.Throws<SqliteException>(() => ExecuteScript(connection, migration));
        Assert.Contains("CONSTRAINT", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqliteSchemaUpgrader_BankCleanup_IsIdempotent()
    {
        using var connection = OpenLegacyBankDb();
        SeedCleanupScenarios(connection);
        AddBankColumns(connection);

        BankAccountCleanup.Apply(connection);
        BankAccountCleanup.EnsureIndexesAndTriggers(connection);
        AssertCleanupResults(connection);

        BankAccountCleanup.Apply(connection);
        BankAccountCleanup.EnsureIndexesAndTriggers(connection);
        AssertCleanupResults(connection);

        Assert.True(ColumnExists(connection, "Bank", "IsDefaultForReceipts"));
        Assert.True(IndexExists(connection, "UX_Bank_Company_Account"));
        Assert.True(TriggerExists(connection, "trg_Bank_AccountId_required_insert"));
    }

    [Fact]
    public void BankAccountCleanup_RepairsSummarySharedDuplicateNamesAndDefaults()
    {
        using var connection = OpenLegacyBankDb();
        SeedCleanupScenarios(connection);
        AddBankColumns(connection);

        BankAccountCleanup.Apply(connection);
        BankAccountCleanup.EnsureIndexesAndTriggers(connection);
        AssertCleanupResults(connection);

        // Preferred codes under 10110 were used for dedicated accounts.
        Assert.True(ScalarLong(connection, @"SELECT COUNT(*) FROM ""Account"" WHERE ""AccountCode"" IN ('10114','10115','10116','10117','10118','10119');") >= 1);
    }

    [Fact]
    public void SaveChanges_RejectsBankWithoutAccountId()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlite(connection)
            .Options;

        using var db = new ApiDbContext(options);
        db.Database.EnsureCreated();

        db.Banks.Add(new Bank
        {
            CompanyId = 1,
            Type = BankTypes.CheckingAccount,
            Name = "No account",
            AccountId = null,
            IsActive = true
        });

        var ex = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        Assert.Contains("Bank.AccountId is required", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Trigger_RejectsInsertWithNullAccountId()
    {
        using var connection = OpenLegacyBankDb();
        SeedEligibleBanks(connection);
        ExecuteScript(connection, File.ReadAllText(Path.Combine(RepoRoot(), "src/Api/d1/migrations/0032_bank_accounts.sql")));

        var ex = Assert.Throws<SqliteException>(() => Execute(connection, @"
INSERT INTO ""Bank"" (""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"", ""IsDefaultForReceipts"", ""IsDefaultForPayments"")
VALUES (1, 1, 'Null account', NULL, 0, 1, 0, 0);
"));
        Assert.Contains("Bank.AccountId is required", ex.Message, StringComparison.Ordinal);
    }

    private static void AssertCleanupResults(SqliteConnection connection)
    {
        Assert.Equal(0L, ScalarLong(connection, @"SELECT COUNT(*) FROM ""Bank"" WHERE ""AccountId"" IS NULL;"));
        Assert.Equal(0L, ScalarLong(connection, @"
SELECT COUNT(*) FROM (
  SELECT ""CompanyId"", ""AccountId"" FROM ""Bank""
  WHERE ""AccountId"" IS NOT NULL
  GROUP BY ""CompanyId"", ""AccountId""
  HAVING COUNT(*) > 1
);"));
        Assert.Equal(0L, ScalarLong(connection, @"
SELECT COUNT(*) FROM (
  SELECT ""CompanyId"", lower(""Name"") FROM ""Bank""
  GROUP BY ""CompanyId"", lower(""Name"")
  HAVING COUNT(*) > 1
);"));
        Assert.Equal(0L, ScalarLong(connection, @"
SELECT COUNT(*) FROM (
  SELECT ""CompanyId"" FROM ""Bank"" WHERE ""IsDefaultForReceipts"" = 1
  GROUP BY ""CompanyId"" HAVING COUNT(*) > 1
);"));
        Assert.Equal(0L, ScalarLong(connection, @"
SELECT COUNT(*) FROM (
  SELECT ""CompanyId"" FROM ""Bank"" WHERE ""IsDefaultForPayments"" = 1
  GROUP BY ""CompanyId"" HAVING COUNT(*) > 1
);"));

        // Summary-account bank without history must have been repointed off 10110.
        Assert.Equal(0L, ScalarLong(connection, @"
SELECT COUNT(*) FROM ""Bank"" b
JOIN ""Account"" a ON a.""Id"" = b.""AccountId""
WHERE a.""AccountCode"" = '10110';"));
    }

    private static SqliteConnection OpenLegacyBankDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection, @"
CREATE TABLE ""Account"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""AccountClassId"" INTEGER NOT NULL,
  ""ParentAccountId"" INTEGER NULL,
  ""DrOrCrSide"" INTEGER NOT NULL,
  ""CompanyId"" INTEGER NOT NULL,
  ""AccountCode"" TEXT NOT NULL,
  ""AccountName"" TEXT NOT NULL,
  ""Description"" TEXT NULL,
  ""IsCash"" INTEGER NOT NULL,
  ""IsContraAccount"" INTEGER NOT NULL
);
CREATE TABLE ""Bank"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""CompanyId"" INTEGER NOT NULL DEFAULT 1,
  ""Type"" INTEGER NOT NULL,
  ""Name"" TEXT NULL,
  ""AccountId"" INTEGER NULL,
  ""BankName"" TEXT NULL,
  ""Number"" TEXT NULL,
  ""Address"" TEXT NULL,
  ""IsDefault"" INTEGER NOT NULL,
  ""IsActive"" INTEGER NOT NULL
);
CREATE TABLE ""GeneralLedgerLine"" (
  ""Id"" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
  ""AccountId"" INTEGER NOT NULL,
  ""CompanyId"" INTEGER NOT NULL,
  ""Amount"" NUMERIC NOT NULL
);
-- Stubs so EnsureAllIndexes can run against a minimal fixture.
CREATE TABLE ""SequenceNumber"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER, ""SequenceNumberType"" INTEGER);
CREATE TABLE ""Customer"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER, ""No"" TEXT);
CREATE TABLE ""Vendor"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER, ""No"" TEXT);
CREATE TABLE ""Item"" (""Id"" INTEGER PRIMARY KEY, ""CompanyId"" INTEGER, ""No"" TEXT);
CREATE TABLE ""__SchemaVersion"" (
  ""Version"" INTEGER NOT NULL PRIMARY KEY,
  ""AppliedAt"" TEXT NOT NULL
);
INSERT INTO ""__SchemaVersion"" (""Version"", ""AppliedAt"") VALUES (1, '2026-01-01');
");
        return connection;
    }

    private static void AddBankColumns(SqliteConnection connection)
    {
        Execute(connection, @"
ALTER TABLE ""Bank"" ADD COLUMN ""RoutingNumber"" TEXT NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""Iban"" TEXT NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""Swift"" TEXT NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""IsDefaultForReceipts"" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ""Bank"" ADD COLUMN ""IsDefaultForPayments"" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE ""Bank"" ADD COLUMN ""OpeningBalance"" NUMERIC NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""OpeningBalanceDate"" TEXT NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""OpeningBalanceGeneralLedgerHeaderId"" INTEGER NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""ArchivedAt"" TEXT NULL;
ALTER TABLE ""Bank"" ADD COLUMN ""CurrencyCode"" TEXT NULL;
");
    }

    private static void SeedEligibleBanks(SqliteConnection connection)
    {
        Execute(connection, @"
INSERT INTO ""Account"" (""Id"", ""AccountClassId"", ""ParentAccountId"", ""DrOrCrSide"", ""CompanyId"", ""AccountCode"", ""AccountName"", ""IsCash"", ""IsContraAccount"")
VALUES
 (1, 1, NULL, 1, 1, '10000', 'Assets', 0, 0),
 (2, 1, 1, 1, 1, '10100', 'Current Assets', 0, 0),
 (5, 1, 2, 1, 1, '10110', 'Cash in Hand and in Bank', 1, 0),
 (16, 1, 5, 1, 1, '10111', 'Regular Checking Account', 0, 0),
 (18, 1, 5, 1, 1, '10113', 'Cash in Hand A/C', 0, 0);
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES
 (1, 1, 1, 'General Fund', 16, 1, 1),
 (2, 1, 3, 'Petty Cash Account', 18, 0, 1);
");
    }

    private static void SeedCleanupScenarios(SqliteConnection connection)
    {
        Execute(connection, @"
INSERT INTO ""Account"" (""Id"", ""AccountClassId"", ""ParentAccountId"", ""DrOrCrSide"", ""CompanyId"", ""AccountCode"", ""AccountName"", ""IsCash"", ""IsContraAccount"")
VALUES
 (1, 1, NULL, 1, 1, '10000', 'Assets', 0, 0),
 (2, 1, 1, 1, 1, '10100', 'Current Assets', 0, 0),
 (5, 1, 2, 1, 1, '10110', 'Cash in Hand and in Bank', 1, 0),
 (16, 1, 5, 1, 1, '10111', 'Regular Checking Account', 0, 0),
 (17, 1, 5, 1, 1, '10112', 'Savings Account', 0, 0),
 (20, 1, 2, 1, 1, '10120', 'Accounts Receivable', 0, 0);

-- Summary account, no GL history → should be repointed
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES (1, 1, 1, 'On Summary', 5, 1, 1);

-- Two banks sharing 10111 → lowest keeps it, other gets dedicated
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES
 (2, 1, 1, 'Shared A', 16, 1, 1),
 (3, 1, 1, 'Shared B', 16, 0, 1);

-- Duplicate names (case-insensitive); second is also non-cash
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES
 (4, 1, 2, 'Dup Name', 17, 0, 1),
 (5, 1, 2, 'dup name', 20, 0, 1);

-- Null AccountId → dedicated account
INSERT INTO ""Bank"" (""Id"", ""CompanyId"", ""Type"", ""Name"", ""AccountId"", ""IsDefault"", ""IsActive"")
VALUES (6, 1, 1, 'No Account', NULL, 0, 1);
");
    }

    private static void ExecuteScript(SqliteConnection connection, string script)
    {
        foreach (var statement in SplitSqlStatements(script))
        {
            if (string.IsNullOrWhiteSpace(statement) || statement.TrimStart().StartsWith("--", StringComparison.Ordinal))
                continue;
            Execute(connection, statement);
        }
    }

    private static IEnumerable<string> SplitSqlStatements(string script)
    {
        using var reader = new StringReader(script);
        string? line;
        var buffer = new StringBuilder();
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;

            buffer.AppendLine(line);
            if (trimmed.EndsWith(";", StringComparison.Ordinal))
            {
                yield return buffer.ToString();
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
            yield return buffer.ToString();
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
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static bool ColumnExists(DbConnection connection, string table, string column)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $c LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$c";
        p.Value = column;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static bool IndexExists(DbConnection connection, string indexName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'index' AND name = $n LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = indexName;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static bool TriggerExists(DbConnection connection, string triggerName)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'trigger' AND name = $n LIMIT 1;";
        var p = cmd.CreateParameter();
        p.ParameterName = "$n";
        p.Value = triggerName;
        cmd.Parameters.Add(p);
        return cmd.ExecuteScalar() != null;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AccountGo.sln"))
                || File.Exists(Path.Combine(dir.FullName, "src", "Api", "Api.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root from " + AppContext.BaseDirectory);
    }
}

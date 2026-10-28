// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System.Data.Common;
using System.Linq;
using Api.Data.Schema;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AccountGo.BackendTests.Entitlements;

public class CompanyActiveModulesTests
{
    [Fact]
    public void DisabledModulesJson_SubtractsFromPlan()
    {
        var company = new Company
        {
            Plan = "accounting_pro",
            DisabledModulesJson = "[\"reports\",\"financials\"]"
        };
        var active = company.GetActiveModules();
        Assert.Contains("sales", active);
        Assert.Contains("purchases", active);
        Assert.DoesNotContain("reports", active);
        Assert.DoesNotContain("financials", active);
    }

    [Fact]
    public void Fallback_UsesActiveModulesJson_WhenDisabledNull()
    {
        // TODO(E5): remove this fallback.
        var company = new Company
        {
            Plan = "accounting_pro",
            DisabledModulesJson = null,
            ActiveModulesJson = "[\"sales\",\"purchases\",\"financials\"]"
        };
        var active = company.GetActiveModules();
        Assert.Equal(new[] { "sales", "financials", "purchases" }, active);
        Assert.DoesNotContain("reports", active);
    }

    [Fact]
    public void DisabledEmptyArray_MeansFullPlan_IgnoresStaleActiveModulesJson()
    {
        var company = new Company
        {
            Plan = "accounting_free",
            DisabledModulesJson = "[]",
            ActiveModulesJson = "[\"sales\",\"purchases\"]"
        };
        Assert.Contains("financials", company.GetActiveModules());
        Assert.Contains("reports", company.GetActiveModules());
    }

    [Fact]
    public void SqliteSchemaUpgrader_BackfillsDisabledModules()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
CREATE TABLE ""Company"" (
  ""Id"" INTEGER PRIMARY KEY,
  ""Plan"" TEXT,
  ""Tier"" TEXT,
  ""ActiveModulesJson"" TEXT,
  ""DisabledModulesJson"" TEXT
);
INSERT INTO ""Company"" (""Id"", ""Plan"", ""Tier"", ""ActiveModulesJson"", ""DisabledModulesJson"")
VALUES (1, 'accounting_pro', 'free', '[""sales"",""purchases"",""financials""]', NULL);
INSERT INTO ""Company"" (""Id"", ""Plan"", ""Tier"", ""ActiveModulesJson"", ""DisabledModulesJson"")
VALUES (2, 'invoicing', 'pro', '[""sales"",""purchases""]', NULL);
INSERT INTO ""Company"" (""Id"", ""Plan"", ""Tier"", ""ActiveModulesJson"", ""DisabledModulesJson"")
VALUES (3, 'accounting_pro', NULL, '[]', NULL);
INSERT INTO ""Company"" (""Id"", ""Plan"", ""Tier"", ""ActiveModulesJson"", ""DisabledModulesJson"")
VALUES (4, 'accounting_pro', NULL, 'not json', NULL);
INSERT INTO ""Company"" (""Id"", ""Plan"", ""Tier"", ""ActiveModulesJson"", ""DisabledModulesJson"")
VALUES (5, 'accounting_pro', NULL, NULL, NULL);
";
            cmd.ExecuteNonQuery();
        }

        var ex = Record.Exception(() =>
        {
            SqliteSchemaUpgrader.BackfillDisabledModules(connection);
            SqliteSchemaUpgrader.NormalizeCompanyPlanKeysAndTier(connection);
        });
        Assert.Null(ex);

        Assert.Equal("[\"reports\"]", ReadDisabled(connection, 1));
        Assert.Equal("[]", ReadDisabled(connection, 2));
        Assert.Equal("[]", ReadDisabled(connection, 3)); // empty array → all modules on
        Assert.Equal("[]", ReadDisabled(connection, 4)); // malformed → all modules on
        Assert.Null(ReadDisabled(connection, 5)); // ActiveModulesJson NULL stays NULL
        Assert.Equal("invoicing_free", ReadPlan(connection, 2));
        Assert.Null(ReadTier(connection, 1));
    }

    private static string? ReadDisabled(DbConnection connection, int id) =>
        ReadCompanyText(connection, "DisabledModulesJson", id);

    private static string? ReadPlan(DbConnection connection, int id) =>
        ReadCompanyText(connection, "Plan", id);

    private static string? ReadTier(DbConnection connection, int id) =>
        ReadCompanyText(connection, "Tier", id);

    private static string? ReadCompanyText(DbConnection connection, string column, int id)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $@"SELECT ""{column}"" FROM ""Company"" WHERE ""Id"" = $id";
        var p = cmd.CreateParameter();
        p.ParameterName = "$id";
        p.Value = id;
        cmd.Parameters.Add(p);
        var value = cmd.ExecuteScalar();
        return value is null or System.DBNull ? null : value.ToString();
    }
}

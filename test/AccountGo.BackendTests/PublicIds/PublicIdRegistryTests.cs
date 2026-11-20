// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Api.Data;
using Api.PublicIds;
using Core.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccountGo.BackendTests.PublicIds;

public sealed class PublicIdRegistryTests
{
    [Fact]
    public void Registry_AllPrefixesAreUniqueAndMatchRegex()
    {
        var registry = new PublicIdRegistry();
        Assert.NotEmpty(registry.ByPrefix);

        var prefixRegex = new Regex(PublicIdGenerator.PrefixPattern, RegexOptions.CultureInvariant);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (prefix, reg) in registry.ByPrefix)
        {
            Assert.True(prefixRegex.IsMatch(prefix), $"Prefix '{prefix}' on {reg.EntityType.Name} does not match {PublicIdGenerator.PrefixPattern}.");
            Assert.True(seen.Add(prefix), $"Duplicate prefix '{prefix}'.");
            Assert.True(typeof(IHasPublicId).IsAssignableFrom(reg.EntityType));
        }
    }

    [Fact]
    public void Registry_EveryIHasPublicIdEntityInDbContextsIsRegisteredAndMatchesBackfillTableList()
    {
        var registry = new PublicIdRegistry();

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var apiDb = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options);
        using var idDb = new ApplicationIdentityDbContext(new DbContextOptionsBuilder<ApplicationIdentityDbContext>().UseSqlite(conn).Options);

        var apiModelSpecs = PublicIdBackfillSql.GetTableSpecs(apiDb, isIdentity: false);
        var idModelSpecs = PublicIdBackfillSql.GetTableSpecs(idDb, isIdentity: true);

        foreach (var entity in apiDb.Model.GetEntityTypes().Where(e => typeof(IHasPublicId).IsAssignableFrom(e.ClrType)))
        {
            Assert.True(registry.TryGetByType(entity.ClrType, out _), $"ApiDb entity {entity.ClrType.Name} is not in PublicIdRegistry.");
        }
        foreach (var entity in idDb.Model.GetEntityTypes().Where(e => typeof(IHasPublicId).IsAssignableFrom(e.ClrType)))
        {
            Assert.True(registry.TryGetByType(entity.ClrType, out _), $"IdentityDb entity {entity.ClrType.Name} is not in PublicIdRegistry.");
        }

        Assert.Equal(
            apiModelSpecs.Select(s => $"{s.TableName}:{s.Prefix}").OrderBy(x => x, StringComparer.Ordinal).ToList(),
            PublicIdBackfillSql.ApiTables.Select(s => $"{s.TableName}:{s.Prefix}").OrderBy(x => x, StringComparer.Ordinal).ToList());

        Assert.Equal(
            idModelSpecs.Select(s => $"{s.TableName}:{s.Prefix}").OrderBy(x => x, StringComparer.Ordinal).ToList(),
            PublicIdBackfillSql.IdentityTables.Select(s => $"{s.TableName}:{s.Prefix}").OrderBy(x => x, StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Generator_Produces100kUniqueRegexValidIds()
    {
        const int count = 100_000;
        var seen = new HashSet<string>(count, StringComparer.Ordinal);
        var regex = new Regex(PublicIdGenerator.FormatPattern, RegexOptions.CultureInvariant);

        for (var i = 0; i < count; i++)
        {
            var id = PublicIdGenerator.New("inv");
            Assert.StartsWith("inv_", id, StringComparison.Ordinal);
            Assert.True(regex.IsMatch(id), $"Generated ID '{id}' did not match {PublicIdGenerator.FormatPattern}.");
            Assert.True(seen.Add(id), $"Duplicate generated ID '{id}' at iteration {i}.");
        }
    }

    [Theory]
    [InlineData("inv_01jd3k9x4m7q2w8e5r6t0y1u2i", true)]
    [InlineData("ws_0123456789abcdef0123456789", true)]
    [InlineData("rcptl_0123456789abcdefghjkmnpqrs", true)]
    [InlineData("INV_01jd3k9x4m7q2w8e5r6t0y1u2i", false)]
    [InlineData("inv_01JD3K9X4M7Q2W8E5R6T0Y1U2I", false)]
    [InlineData("a_01jd3k9x4m7q2w8e5r6t0y1u2i", false)]
    [InlineData("abcdef_01jd3k9x4m7q2w8e5r6t0y1u2i", false)]
    [InlineData("inv_short", false)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void FormatValidation_EnforcesStrictLowercasePattern(string? candidate, bool expected)
    {
        Assert.Equal(expected, PublicIdGenerator.IsValidFormat(candidate));
    }

    [Fact]
    public void BackfillSql_SqliteProducesRegexValidUniqueIds()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE ""Sample"" (""Id"" INTEGER PRIMARY KEY AUTOINCREMENT, ""PublicId"" TEXT NOT NULL DEFAULT '');
                INSERT INTO ""Sample"" (""PublicId"") VALUES (''), (''), (''), (''), ('');";
            cmd.ExecuteNonQuery();
        }

        var sql = PublicIdBackfillSql.UpdateStatement(PublicIdSqlDialect.Sqlite, "Sample", "inv");
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            Assert.Equal(5, cmd.ExecuteNonQuery());
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""PublicId"" FROM ""Sample"";";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetString(0);
                Assert.StartsWith("inv_", id, StringComparison.Ordinal);
                Assert.True(PublicIdGenerator.IsValidFormat(id), $"Backfilled SQLite ID '{id}' is invalid.");
                Assert.True(ids.Add(id), $"Duplicate backfilled ID '{id}'.");
            }
        }
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public void EveryDocumentType_IsMappedToAPrefix_OrDeliberatelyExempt()
    {
        var registry = new PublicIdRegistry();
        foreach (var name in Enum.GetNames<Core.Domain.DocumentTypes>())
            Assert.True(registry.TryGetPrefixForTypeName(name, out _) || registry.IsExemptTypeName(name),
                $"Document type '{name}' has no public id prefix and is not listed as exempt: a ledger row of that type would fail to serialize.");
    }

    [Theory]
    [InlineData("invoice", "inv")]
    [InlineData("invoiceVoid", "inv")]
    [InlineData("bill", "bill")]
    [InlineData("billVoid", "bill")]
    [InlineData("invoicePayment", "inv")]
    [InlineData("billPayment", "bill")]
    [InlineData("customerCredit", "cn")]
    [InlineData("customerCreditVoid", "cn")]
    [InlineData("customerRefund", "cn")]
    [InlineData("supplierCredit", "scn")]
    [InlineData("supplierCreditVoid", "scn")]
    [InlineData("supplierRefund", "scn")]
    public void TaxReportDocumentKinds_MapToTheirDocumentPrefix(string kind, string prefix)
    {
        Assert.True(new PublicIdRegistry().TryGetPrefixForTypeName(kind, out var actual));
        Assert.Equal(prefix, actual);
    }
}

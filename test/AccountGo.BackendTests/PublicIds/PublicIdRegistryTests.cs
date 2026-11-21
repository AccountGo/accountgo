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
            // DocumentCaptures is introduced by its own migration after the historical public-ID backfill migration.
            apiModelSpecs.Where(s => s.TableName != "DocumentCaptures").Select(s => $"{s.TableName}:{s.Prefix}").OrderBy(x => x, StringComparer.Ordinal).ToList(),
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

    /// <summary>
    /// A DTO id that carries no <c>[PublicId]</c> attribute travels as a bare integer on the wire, and the test helpers
    /// serialize with the same attributes as the API, so the gap is invisible to round-trip tests. Every id-like integer
    /// on a wire type must be annotated or be a known non-entity code.
    /// </summary>
    [Fact]
    public void Dtos_EveryIdLikeIntegerCarriesAPublicIdAttribute()
    {
        var nonEntityCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "SalesOrder.StatusId", "SalesInvoice.StatusId", "SalesQuotation.StatusId", "PurchaseOrder.StatusId",
            "MasterGeneralLedger.GroupId", "Account.AccountClassId", "Status.Id",
            // Internal report row, not returned by any controller; and a classification code on a COA import preview.
            "MasterGeneralLedger.CurrencyId", "CoaImportRowPreviewDto.AccountClassId"
        };
        var assembly = typeof(Dto.Common.PublicIdAttribute).Assembly;
        var missing = new List<string>();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass && t.IsPublic && !t.IsAbstract))
        {
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            {
                var pt = property.PropertyType;
                var isInt = pt == typeof(int) || pt == typeof(int?);
                var isIntList = pt == typeof(int[])
                    || (pt.IsGenericType && pt.GetGenericArguments().Length == 1
                        && (pt.GetGenericArguments()[0] == typeof(int) || pt.GetGenericArguments()[0] == typeof(int?))
                        && typeof(System.Collections.IEnumerable).IsAssignableFrom(pt));
                var idLike = property.Name == "Id" || property.Name.EndsWith("Id", StringComparison.Ordinal) || property.Name.EndsWith("Ids", StringComparison.Ordinal);
                if (!(isInt && idLike || isIntList)) continue;
                if (property.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIgnoreAttribute), true).Length > 0
                    || property.GetCustomAttributes(typeof(Newtonsoft.Json.JsonIgnoreAttribute), true).Length > 0) continue;
                var annotated = property.GetCustomAttributes(typeof(Dto.Common.PublicIdAttribute), true).Length > 0
                    || property.GetCustomAttributes(typeof(Dto.Common.PublicIdByTypeAttribute), true).Length > 0;
                if (!annotated && !nonEntityCodes.Contains($"{type.Name}.{property.Name}"))
                    missing.Add($"{type.FullName}.{property.Name}");
            }
        }
        Assert.True(missing.Count == 0, "Wire DTO ids without a [PublicId] attribute:\n" + string.Join("\n", missing));
    }

    /// <summary>
    /// The by-type object converter writes properties itself, so property-level serializer settings are not applied to a
    /// DTO that has a [PublicIdByType] property. Keep those DTOs plain until the converter honours them.
    /// </summary>
    [Fact]
    public void ByTypeDtos_DoNotUsePropertyLevelSerializerSettings()
    {
        var assembly = typeof(Dto.Common.PublicIdAttribute).Assembly;
        var offenders = new List<string>();
        foreach (var type in assembly.GetTypes().Where(t => t.IsClass))
        {
            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (!props.Any(p => p.GetCustomAttributes(typeof(Dto.Common.PublicIdByTypeAttribute), true).Length > 0)) continue;
            foreach (var p in props)
            {
                var jp = (Newtonsoft.Json.JsonPropertyAttribute?)p.GetCustomAttributes(typeof(Newtonsoft.Json.JsonPropertyAttribute), true).FirstOrDefault();
                if (p.GetCustomAttributes(typeof(Newtonsoft.Json.JsonConverterAttribute), true).Length > 0
                    || (jp != null && (jp.NullValueHandling != Newtonsoft.Json.NullValueHandling.Include || jp.DefaultValueHandling != Newtonsoft.Json.DefaultValueHandling.Include || jp.Order != 0)))
                    offenders.Add($"{type.FullName}.{p.Name}");
            }
            foreach (var m in type.GetMethods().Where(m => m.Name.StartsWith("ShouldSerialize", StringComparison.Ordinal)))
                offenders.Add($"{type.FullName}.{m.Name}");
        }
        Assert.True(offenders.Count == 0, "Not applied by the by-type converter:\n" + string.Join("\n", offenders));
    }
}

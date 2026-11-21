// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core.Domain;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class CoaTemplatesCashRangeTests
{
    [Fact]
    public void BaseCashRangeAccountCodes_MatchesCoaCsv_10100To10999()
    {
        var csvPath = Path.Combine(RepoRoot(), "src", "Api", "Data", "coa.csv");
        var fromCsv = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(csvPath).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var code = line.Split(',')[0].Trim();
            if (!int.TryParse(code, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
                continue;
            if (numeric >= 10100 && numeric <= 10999)
                fromCsv.Add(code);
        }

        var fromTemplate = CoaTemplates.BaseCashRangeAccountCodes.ToHashSet(StringComparer.Ordinal);
        Assert.Equal(fromCsv, fromTemplate);
    }

    [Fact]
    public void BaseChart_HasGeneralInsuranceExpenseAndLoansPayable_UnderTheirParents_WithUniqueCodes()
    {
        var csvPath = Path.Combine(RepoRoot(), "src", "Api", "Data", "coa.csv");
        var rows = File.ReadLines(csvPath).Skip(1)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','))
            .ToList();

        Assert.Equal(rows.Count, rows.Select(r => r[0].Trim()).Distinct(StringComparer.Ordinal).Count());

        var insurance = Assert.Single(rows, r => r[0] == "50127");
        Assert.Equal("Insurance Expenses-General", insurance[1]);
        Assert.Equal("50100", insurance[2]);
        Assert.Equal("5", insurance[3]);

        var loans = Assert.Single(rows, r => r[0] == "20510");
        Assert.Equal("Loans Payable", loans[1]);
        Assert.Equal("20000", loans[2]);
        Assert.Equal("2", loans[3]);
        Assert.Equal("CR", loans[7].Trim());
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

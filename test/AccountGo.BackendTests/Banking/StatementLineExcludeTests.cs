// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class StatementLineExcludeTests
{
    [Fact]
    public void Exclude_and_Include_move_line_between_tabs_and_block_match()
    {
        using var harness = NewHarness(out var bank, out var line);

        var excluded = harness.Statements.Exclude(line.Id, new ExcludeBankStatementLineRequest
        {
            Reason = (int)BankStatementExclusionReasons.Personal
        }, "tester");
        Assert.True(excluded.IsExcluded);
        Assert.Equal((int)BankStatementExclusionReasons.Personal, excluded.ExclusionReason);

        var listExcluded = harness.Statements.ListLines(bank.Id, BankStatementImportService.StatusExcluded);
        Assert.Equal(1, listExcluded.ExcludedCount);
        Assert.Equal(0, listExcluded.UnmatchedCount);
        Assert.Single(listExcluded.Lines);

        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(line.Id, new MatchBankStatementLineRequest()));

        var included = harness.Statements.Include(line.Id);
        Assert.False(included.IsExcluded);
        Assert.Null(included.ExclusionReason);

        var listUnmatched = harness.Statements.ListLines(bank.Id, BankStatementImportService.StatusUnmatched);
        Assert.Equal(1, listUnmatched.UnmatchedCount);
        Assert.Equal(0, listUnmatched.ExcludedCount);
    }

    [Fact]
    public void Exclude_requires_unmatch_first_and_Other_needs_note()
    {
        using var harness = NewHarness(out var bank, out var line);
        var gl = harness.Post(bank.AccountId, DrOrCrSide.Cr, 25m, BankingTestHarness.Utc(2026, 6, 1));
        harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = gl.Id });

        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Exclude(line.Id, new ExcludeBankStatementLineRequest
            {
                Reason = (int)BankStatementExclusionReasons.Duplicate
            }, "t"));

        harness.Statements.Unmatch(line.Id);
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Exclude(line.Id, new ExcludeBankStatementLineRequest
            {
                Reason = (int)BankStatementExclusionReasons.Other
            }, "t"));

        var excluded = harness.Statements.Exclude(line.Id, new ExcludeBankStatementLineRequest
        {
            Reason = (int)BankStatementExclusionReasons.Other,
            Note = "Shared account"
        }, "t");
        Assert.Equal("Shared account", excluded.ExclusionNote);
    }

    [Fact]
    public void CreateReversingEntry_clears_statement_match()
    {
        using var harness = NewHarness(out var bank, out var line);
        var gl = harness.Post(bank.AccountId, DrOrCrSide.Cr, 25m, BankingTestHarness.Utc(2026, 6, 2));
        harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = gl.Id });
        Assert.True(harness.Repos.StatementLines.GetById(line.Id).MatchedGeneralLedgerLineId.HasValue);

        // Mimic FinancialService.CreateReversingEntry's ClearStatementMatches.
        var matched = harness.Repos.StatementLines.Table
            .Where(l => l.MatchedGeneralLedgerLineId == gl.Id)
            .ToList();
        foreach (var row in matched)
        {
            row.MatchedGeneralLedgerLineId = null;
            harness.Repos.StatementLines.Update(row);
        }

        Assert.Null(harness.Repos.StatementLines.GetById(line.Id).MatchedGeneralLedgerLineId);
    }

    private static BankingTestHarness NewHarness(out BankAccountDtoLite bank, out BankStatementLine line)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        var created = harness.CreateBank("Checking");
        bank = new BankAccountDtoLite(created.Id, created.AccountId!.Value);

        var import = new BankStatementImport
        {
            CompanyId = 1,
            BankId = bank.Id,
            Source = BankStatementSources.Csv,
            FileName = "t.csv",
            ImportedAt = DateTime.UtcNow,
            ImportedBy = "t"
        };
        harness.Repos.StatementImports.Insert(import);
        line = new BankStatementLine
        {
            CompanyId = 1,
            BankId = bank.Id,
            BankStatementImportId = import.Id,
            Source = BankStatementSources.Csv,
            ExternalId = "ex-1",
            PostedDate = BankingTestHarness.Utc(2026, 6, 1),
            Amount = -25m,
            Payee = "Coffee"
        };
        harness.Repos.StatementLines.Insert(line);
        return harness;
    }

    private sealed record BankAccountDtoLite(int Id, int AccountId);
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankStatementImportServiceTests
{
    private static readonly BankStatementColumnMap Map = new()
    {
        DateColumn = 0,
        AmountColumn = 1,
        PayeeColumn = 2,
        MemoColumn = 3,
        ReferenceColumn = 4
    };

    private static BankStatementImportRequest Csv(string body, string name = "stmt.csv") => new()
    {
        Source = (int)BankStatementSources.Csv,
        FileName = name,
        Content = "Date,Amount,Payee,Memo,Ref\n" + body,
        ColumnMap = Map
    };

    private static BankingTestHarness NewHarness(out BankAccountDtoLite bank)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        harness.SeedFeeAndInterestAccounts();
        var created = harness.CreateBank("Checking");
        bank = new BankAccountDtoLite(created.Id, created.AccountId!.Value);
        return harness;
    }

    private sealed record BankAccountDtoLite(int Id, int AccountId);

    [Fact]
    public void Commit_Csv_StoresLines_AndAutoMatchesUniqueCandidate()
    {
        using var harness = NewHarness(out var bank);
        var glLine = harness.Post(bank.AccountId, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2026, 10, 1));
        harness.Post(bank.AccountId, DrOrCrSide.Cr, 40m, BankingTestHarness.Utc(2026, 10, 2));

        var result = harness.Statements.Commit(bank.Id, Csv(
            "2026-10-02,100.00,Customer,Deposit,\n" +
            "2026-10-03,-40.00,Shop,Supplies,\n" +
            "2026-10-04,-7.25,Bank,Fee,\n"), "tester");

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.Duplicates);
        Assert.Equal(2, result.AutoMatched);

        var list = harness.Statements.ListLines(bank.Id, "all");
        Assert.Equal(3, list.Lines.Count);
        var deposit = list.Lines.Single(l => l.Amount == 100m);
        Assert.True(deposit.IsMatched);
        Assert.Equal(glLine.Id, deposit.MatchedGeneralLedgerLineId);
        Assert.Equal(1, list.UnmatchedCount);
        Assert.Equal(2, list.MatchedCount);

        var import = Assert.Single(harness.Repos.StatementImports.Table);
        Assert.Equal("tester", import.ImportedBy);
        Assert.Equal(BankStatementSources.Csv, import.Source);
        Assert.NotNull(import.ColumnMapJson);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(-2, true)]
    [InlineData(-3, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(-4, false)]
    public void Matching_RespectsThreeDayWindow(int dayOffset, bool expectMatch)
    {
        using var harness = NewHarness(out var bank);
        var ledgerDate = BankingTestHarness.Utc(2026, 10, 10);
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 55m, ledgerDate);
        var statementDate = ledgerDate.AddDays(dayOffset);

        var result = harness.Statements.Commit(bank.Id, Csv(statementDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ",55.00,X,,\n"), "t");

        Assert.Equal(expectMatch ? 1 : 0, result.AutoMatched);
    }

    [Fact]
    public void Matching_AmbiguousCandidatesStayUnmatched()
    {
        using var harness = NewHarness(out var bank);
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 20m, BankingTestHarness.Utc(2026, 10, 5));
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 20m, BankingTestHarness.Utc(2026, 10, 6));

        var result = harness.Statements.Commit(bank.Id, Csv("2026-10-05,20.00,A,,\n"), "t");

        Assert.Equal(0, result.AutoMatched);
        var line = Assert.Single(harness.Statements.ListLines(bank.Id, "unmatched").Lines);
        Assert.Equal(2, line.Candidates.Count);
    }

    [Fact]
    public void Matching_IsOneToOne_TwoStatementLinesCompetingForOneLedgerLine()
    {
        using var harness = NewHarness(out var bank);
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 20m, BankingTestHarness.Utc(2026, 10, 5));

        var result = harness.Statements.Commit(bank.Id, Csv(
            "2026-10-05,20.00,A,one,\n2026-10-06,20.00,B,two,\n"), "t");

        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.AutoMatched);
        Assert.Equal(2, harness.Statements.ListLines(bank.Id, "unmatched").Lines.Count);
    }

    [Fact]
    public void Matching_IgnoresClearedLines_WrongDirection_AndOtherAccounts()
    {
        using var harness = NewHarness(out var bank);
        var cleared = harness.Post(bank.AccountId, DrOrCrSide.Dr, 30m, BankingTestHarness.Utc(2026, 10, 5));
        cleared.BankReconciliationId = 999;
        harness.Post(bank.AccountId, DrOrCrSide.Cr, 45m, BankingTestHarness.Utc(2026, 10, 5));
        harness.Post(harness.RequireAccount("50100").Id, DrOrCrSide.Dr, 60m, BankingTestHarness.Utc(2026, 10, 5));

        var result = harness.Statements.Commit(bank.Id, Csv(
            "2026-10-05,30.00,A,,\n2026-10-05,45.00,B,,\n2026-10-05,60.00,C,,\n"), "t");

        Assert.Equal(3, result.Imported);
        Assert.Equal(0, result.AutoMatched);
    }

    [Fact]
    public void Commit_DedupesByExternalId_AcrossImportsAndWithinFile()
    {
        using var harness = NewHarness(out var bank);
        var body = "2026-10-01,10.00,A,m,\n2026-10-02,20.00,B,m,\n";

        var first = harness.Statements.Commit(bank.Id, Csv(body), "t");
        var second = harness.Statements.Commit(bank.Id, Csv(body + "2026-10-03,30.00,C,m,\n"), "t");
        var third = harness.Statements.Commit(bank.Id, Csv(body), "t");

        Assert.Equal(2, first.Imported);
        Assert.Equal(1, second.Imported);
        Assert.Equal(2, second.Duplicates);
        Assert.Equal(0, third.Imported);
        Assert.Equal(2, third.Duplicates);
        Assert.Equal(0, third.ImportId);
        Assert.Equal(3, harness.Repos.StatementLines.Table.Count());
        Assert.Equal(2, harness.Repos.StatementImports.Table.Count());
    }

    [Fact]
    public void Commit_Ofx_DedupesOnFitId_AndSeparatesSources()
    {
        using var harness = NewHarness(out var bank);
        const string ofx =
            "<OFX><BANKTRANLIST>" +
            "<STMTTRN><DTPOSTED>20261001<TRNAMT>10.00<FITID>A1<NAME>One</STMTTRN>" +
            "<STMTTRN><DTPOSTED>20261001<TRNAMT>10.00<FITID>A1<NAME>One again</STMTTRN>" +
            "<STMTTRN><DTPOSTED>20261002<TRNAMT>-5.00<FITID>A2<NAME>Two</STMTTRN>" +
            "</BANKTRANLIST></OFX>";
        var request = new BankStatementImportRequest { Source = (int)BankStatementSources.Ofx, FileName = "x.ofx", Content = ofx };

        var first = harness.Statements.Commit(bank.Id, request, "t");
        var again = harness.Statements.Commit(bank.Id, request, "t");

        Assert.Equal(2, first.Imported);
        Assert.Equal(1, first.Duplicates);
        Assert.Equal(0, again.Imported);
        Assert.Equal(3, again.Duplicates);
        Assert.All(harness.Repos.StatementLines.Table, l => Assert.Equal(BankStatementSources.Ofx, l.Source));
    }

    [Fact]
    public void Preview_WritesNothing_AndClassifiesRows()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv("2026-10-01,10.00,A,m,\n"), "t");

        var preview = harness.Statements.Preview(bank.Id, Csv(
            "2026-10-01,10.00,A,m,\n2026-10-02,20.00,B,m,\nbad,1,x,,\n"));

        Assert.Equal(3, preview.TotalRows);
        Assert.Equal(1, preview.ReadyRows);
        Assert.Equal(1, preview.DuplicateRows);
        Assert.Equal(1, preview.ErrorRows);
        Assert.Equal(new[] { "duplicate", "ok", "error" }, preview.Rows.Select(r => r.Status));
        Assert.Single(harness.Repos.StatementLines.Table);
    }

    [Fact]
    public void Commit_RefusesArchivedBank_AndManualSource()
    {
        using var harness = NewHarness(out var bank);
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Commit(bank.Id, new BankStatementImportRequest { Source = 3, Content = "x" }, "t"));

        harness.Repos.Banks.GetById(bank.Id).ArchivedAt = DateTime.UtcNow;
        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Commit(bank.Id, Csv("2026-10-01,10.00,A,m,\n"), "t"));
        Assert.Contains("archived", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManualMatch_AndUnmatch_RoundTrip_WithValidation()
    {
        using var harness = NewHarness(out var bank);
        var ledger = harness.Post(bank.AccountId, DrOrCrSide.Dr, 80m, BankingTestHarness.Utc(2026, 10, 1));
        var tooFar = harness.Post(bank.AccountId, DrOrCrSide.Dr, 80m, BankingTestHarness.Utc(2026, 10, 20));
        harness.Statements.Commit(bank.Id, Csv("2026-10-02,80.00,A,,\n"), "t");
        var line = Assert.Single(harness.Repos.StatementLines.Table);
        // Two same-amount ledger lines but only one inside the window → auto-match took the first.
        Assert.Equal(ledger.Id, line.MatchedGeneralLedgerLineId);

        var unmatched = harness.Statements.Unmatch(line.Id);
        Assert.False(unmatched.IsMatched);
        Assert.Throws<InvalidOperationException>(() => harness.Statements.Unmatch(line.Id));

        // Manual pick may sit outside the window.
        var matched = harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = tooFar.Id });
        Assert.Equal(tooFar.Id, matched.MatchedGeneralLedgerLineId);
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = ledger.Id }));

        harness.Statements.Unmatch(line.Id);
        var wrongAmount = harness.Post(bank.AccountId, DrOrCrSide.Dr, 81m, BankingTestHarness.Utc(2026, 10, 2));
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = wrongAmount.Id }));
        var otherAccount = harness.Post(harness.RequireAccount("50100").Id, DrOrCrSide.Dr, 80m, BankingTestHarness.Utc(2026, 10, 2));
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(line.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = otherAccount.Id }));
    }

    [Fact]
    public void Match_WithoutLedgerLine_AutoMatchesOnlyUniqueCandidate()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv("2026-10-02,80.00,A,,\n"), "t");
        var line = Assert.Single(harness.Repos.StatementLines.Table);

        var none = Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(line.Id, new MatchBankStatementLineRequest()));
        Assert.Contains("No single ledger line", none.Message);

        var ledger = harness.Post(bank.AccountId, DrOrCrSide.Dr, 80m, BankingTestHarness.Utc(2026, 10, 3));
        var matched = harness.Statements.Match(line.Id, new MatchBankStatementLineRequest());
        Assert.Equal(ledger.Id, matched.MatchedGeneralLedgerLineId);
    }

    [Fact]
    public void CreateTransaction_PostsFee_AndLinksLedgerLine()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv("2026-10-04,-7.25,Bank,Monthly fee,REF1\n"), "t");
        var line = Assert.Single(harness.Repos.StatementLines.Table);

        var created = harness.Statements.CreateTransaction(line.Id, new CreateTransactionFromStatementLineRequest
        {
            Kind = (int)BankTransactionKinds.Fee
        });

        Assert.True(created.IsMatched);
        var tx = Assert.Single(harness.Repos.BankTransactions.Table);
        Assert.Equal(BankTransactionKinds.Fee, tx.Kind);
        Assert.Equal(7.25m, tx.Amount);
        Assert.Equal(new DateTime(2026, 10, 4), tx.Date);
        Assert.Equal("REF1", tx.ReferenceNo);
        var glLine = harness.Repos.GlLines.GetById(created.MatchedGeneralLedgerLineId!.Value);
        Assert.Equal(bank.AccountId, glLine.AccountId);
        Assert.Equal(DrOrCrSide.Cr, glLine.DrCr);
        Assert.Equal(-7.25m, harness.AccountBalance(bank.AccountId));

        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.CreateTransaction(line.Id, new CreateTransactionFromStatementLineRequest
            {
                Kind = (int)BankTransactionKinds.Fee
            }));
    }

    [Fact]
    public void CreateTransaction_ChecksDirection_AndSupportsAdjustmentAndMoneyIn()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv(
            "2026-10-04,15.00,Bank,Interest,\n2026-10-05,-3.00,Bank,Oops,\n2026-10-06,9.00,Cust,Deposit,\n"), "t");
        var lines = harness.Repos.StatementLines.Table.OrderBy(l => l.Id).ToList();
        var contra = harness.RequireAccount("50100").Id;

        // Fee is a payment kind: refuse on a deposit.
        Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.CreateTransaction(lines[0].Id, new CreateTransactionFromStatementLineRequest
            {
                Kind = (int)BankTransactionKinds.Fee
            }));

        var interest = harness.Statements.CreateTransaction(lines[0].Id, new CreateTransactionFromStatementLineRequest
        {
            Kind = (int)BankTransactionKinds.Interest
        });
        Assert.True(interest.IsMatched);

        var adjustment = harness.Statements.CreateTransaction(lines[1].Id, new CreateTransactionFromStatementLineRequest
        {
            Kind = (int)BankTransactionKinds.Adjustment,
            ContraAccountId = contra
        });
        Assert.True(adjustment.IsMatched);

        var moneyIn = harness.Statements.CreateTransaction(lines[2].Id, new CreateTransactionFromStatementLineRequest
        {
            Kind = (int)BankTransactionKinds.MoneyIn,
            ContraAccountId = contra
        });
        Assert.True(moneyIn.IsMatched);
        Assert.Equal(15m - 3m + 9m, harness.AccountBalance(bank.AccountId));
    }

    [Fact]
    public void ListLines_FiltersByStatus_AndRejectsUnknownStatus()
    {
        using var harness = NewHarness(out var bank);
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 10m, BankingTestHarness.Utc(2026, 10, 1));
        harness.Statements.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n2026-10-02,20.00,B,,\n"), "t");

        Assert.Single(harness.Statements.ListLines(bank.Id, null).Lines);
        Assert.Single(harness.Statements.ListLines(bank.Id, "unmatched").Lines);
        Assert.Single(harness.Statements.ListLines(bank.Id, "matched").Lines);
        Assert.Equal(2, harness.Statements.ListLines(bank.Id, "all").Lines.Count);
        Assert.Throws<InvalidOperationException>(() => harness.Statements.ListLines(bank.Id, "bogus"));
    }

    [Fact]
    public void ListLines_PagesServerSide()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv(
            "2026-10-01,10.00,A,,\n" +
            "2026-10-02,20.00,B,,\n" +
            "2026-10-03,30.00,C,,\n"), "t");

        var page1 = harness.Statements.ListLines(bank.Id, "all", page: 1, pageSize: 2);
        var page2 = harness.Statements.ListLines(bank.Id, "all", page: 2, pageSize: 2);

        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(1, page1.Page);
        Assert.Equal(2, page1.PageSize);
        Assert.Equal(2, page1.Lines.Count);
        Assert.Equal(10m, page1.Lines[0].Amount);
        Assert.Equal(20m, page1.Lines[1].Amount);

        Assert.Equal(3, page2.TotalCount);
        Assert.Equal(2, page2.Page);
        Assert.Single(page2.Lines);
        Assert.Equal(30m, page2.Lines[0].Amount);

        var clamped = harness.Statements.ListLines(bank.Id, "all", page: 0, pageSize: 10_000);
        Assert.Equal(1, clamped.Page);
        Assert.Equal(BankStatementImportService.MaxListPageSize, clamped.PageSize);
        Assert.Equal(3, clamped.Lines.Count);
    }

    [Fact]
    public void TenantIsolation_OtherCompanyCannotSeeOrChangeLines()
    {
        using var harness = NewHarness(out var bank);
        harness.Statements.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n"), "t");
        var line = Assert.Single(harness.Repos.StatementLines.Table);
        Assert.Equal(1, line.CompanyId);

        var other = harness.BuildServices(companyId: 2).Statements;

        Assert.Throws<TenantEntityNotFoundException>(() => other.ListLines(bank.Id, "all"));
        Assert.Throws<TenantEntityNotFoundException>(() => other.Preview(bank.Id, Csv("2026-10-01,10.00,A,,\n")));
        Assert.Throws<TenantEntityNotFoundException>(() => other.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n"), "t"));
        Assert.Throws<TenantEntityNotFoundException>(() => other.Match(line.Id, new MatchBankStatementLineRequest()));
        Assert.Throws<TenantEntityNotFoundException>(() => other.Unmatch(line.Id));
        Assert.Throws<TenantEntityNotFoundException>(() =>
            other.CreateTransaction(line.Id, new CreateTransactionFromStatementLineRequest { Kind = (int)BankTransactionKinds.Fee }));
    }

    private static GeneralLedgerLine PostReversal(BankingTestHarness harness, GeneralLedgerLine original, DateTime date)
    {
        var header = new GeneralLedgerHeader
        {
            Id = harness.Repos.NextGlHeaderId(),
            CompanyId = 1,
            DocumentType = DocumentTypes.JournalEntry,
            Date = date.Date,
            Description = "void",
            ReversedGeneralLedgerHeaderId = original.GeneralLedgerHeaderId
        };
        harness.Repos.GlHeaders.Insert(header);
        var line = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = header.Id,
            AccountId = original.AccountId,
            DrCr = original.DrCr == DrOrCrSide.Dr ? DrOrCrSide.Cr : DrOrCrSide.Dr,
            Amount = original.Amount
        };
        harness.Repos.GlLines.Insert(line);
        return line;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(60)]
    public void Matching_SkipsVoidedEntry_WhereverTheReversalIsDated(int reversalOffsetDays)
    {
        using var harness = NewHarness(out var bank);
        var original = harness.Post(bank.AccountId, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2026, 10, 5));
        PostReversal(harness, original, BankingTestHarness.Utc(2026, 10, 5).AddDays(reversalOffsetDays));

        var result = harness.Statements.Commit(bank.Id, Csv("2026-10-05,100.00,Customer,,\n"), "t");

        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.AutoMatched);
        var line = Assert.Single(harness.Statements.ListLines(bank.Id, "unmatched").Lines);
        Assert.Empty(line.Candidates);
    }

    [Fact]
    public void Matching_SkipsReversalEntry_WhenOnlyTheReversalIsInTheWindow()
    {
        using var harness = NewHarness(out var bank);
        var original = harness.Post(bank.AccountId, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2026, 8, 1));
        PostReversal(harness, original, BankingTestHarness.Utc(2026, 10, 5));

        var result = harness.Statements.Commit(bank.Id, Csv("2026-10-05,-100.00,Customer,,\n"), "t");

        Assert.Equal(0, result.AutoMatched);
        Assert.Empty(Assert.Single(harness.Statements.ListLines(bank.Id, "unmatched").Lines).Candidates);
    }

    [Fact]
    public void ManualMatch_RefusesVoidedEntryAndItsReversal()
    {
        using var harness = NewHarness(out var bank);
        var original = harness.Post(bank.AccountId, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2026, 10, 5));
        var reversal = PostReversal(harness, original, BankingTestHarness.Utc(2026, 12, 1));
        harness.Statements.Commit(bank.Id, Csv("2026-10-05,100.00,Customer,,\n2026-10-05,-100.00,Refund,,\n"), "t");
        Assert.All(harness.Repos.StatementLines.Table, l => Assert.Null(l.MatchedGeneralLedgerLineId));
        var deposit = harness.Repos.StatementLines.Table.Single(l => l.Amount == 100m);
        var payment = harness.Repos.StatementLines.Table.Single(l => l.Amount == -100m);

        var voided = Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(deposit.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = original.Id }));
        var reversed = Assert.Throws<InvalidOperationException>(() =>
            harness.Statements.Match(payment.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = reversal.Id }));

        Assert.Contains("voided", voided.Message);
        Assert.Contains("voided", reversed.Message);
        // A ledger line from an entry that was never voided is still matchable.
        var healthy = harness.Post(bank.AccountId, DrOrCrSide.Dr, 100m, BankingTestHarness.Utc(2026, 10, 5));
        var matched = harness.Statements.Match(deposit.Id, new MatchBankStatementLineRequest { GeneralLedgerLineId = healthy.Id });
        Assert.Equal(healthy.Id, matched.MatchedGeneralLedgerLineId);
    }

    private static (Mock<IRepository<BankStatementLine>> Repo, List<BankStatementLine> Stored) BatchRepo()
    {
        var stored = new List<BankStatementLine>();
        var repo = new Mock<IRepository<BankStatementLine>>();
        repo.SetupGet(r => r.Table).Returns(() => stored.AsQueryable());
        repo.Setup(r => r.Insert(It.IsAny<IEnumerable<BankStatementLine>>()))
            .Callback<IEnumerable<BankStatementLine>>(batch =>
            {
                foreach (var line in batch)
                {
                    line.Id = stored.Count + 1;
                    stored.Add(line);
                }
            });
        return (repo, stored);
    }

    private static BankStatementImportService ServiceOver(BankingTestHarness harness, IRepository<BankStatementLine> lines) =>
        new(
            harness.Repos.StatementImports,
            lines,
            harness.Repos.Banks,
            harness.Repos.GlLines,
            harness.Repos.GlHeaders,
            harness.Transactions,
            new BankingTestHarness.FakeTenant(1));

    [Fact]
    public void Commit_AutoMatchedCount_OnlyIncludesThisImport()
    {
        using var harness = NewHarness(out var bank);
        // Earlier import left an unmatched fee; the ledger entry that clears it arrives before the next file.
        harness.Statements.Commit(bank.Id, Csv("2026-10-01,-7.25,Bank,Fee,\n"), "t1");
        harness.Post(bank.AccountId, DrOrCrSide.Cr, 7.25m, BankingTestHarness.Utc(2026, 10, 1));
        harness.Post(bank.AccountId, DrOrCrSide.Dr, 50m, BankingTestHarness.Utc(2026, 10, 2));

        var second = harness.Statements.Commit(bank.Id, Csv("2026-10-02,50.00,Customer,Deposit,\n"), "t2");

        Assert.Equal(1, second.Imported);
        Assert.Equal(1, second.AutoMatched);
        var list = harness.Statements.ListLines(bank.Id, "all");
        Assert.Equal(2, list.MatchedCount);
        Assert.Equal(0, list.UnmatchedCount);
    }

    [Fact]
    public void Commit_InsertsAllLinesInOneBatch()
    {
        using var harness = NewHarness(out var bank);
        var (repo, stored) = BatchRepo();
        var service = ServiceOver(harness, repo.Object);

        var result = service.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n2026-10-02,20.00,B,,\n2026-10-03,30.00,C,,\n"), "t");

        Assert.Equal(3, result.Imported);
        Assert.Equal(3, stored.Count);
        repo.Verify(r => r.Insert(It.IsAny<IEnumerable<BankStatementLine>>()), Times.Once);
        repo.Verify(r => r.Insert(It.IsAny<BankStatementLine>()), Times.Never);
    }

    [Theory]
    [InlineData("Violation of UNIQUE KEY constraint. Cannot insert duplicate key in object 'dbo.BankStatementLine'. The duplicate key value is (1, 1, 1, csv:abc). unique index 'UX_BankStatementLine_Company_Bank_Source_External'")]
    [InlineData("SQLite Error 19: 'UNIQUE constraint failed: BankStatementLine.CompanyId, BankStatementLine.BankId, BankStatementLine.Source, BankStatementLine.ExternalId'.")]
    public void Commit_ConcurrentDuplicateImport_ReportsAlreadyImported(string databaseMessage)
    {
        using var harness = NewHarness(out var bank);
        var repo = new Mock<IRepository<BankStatementLine>>();
        repo.SetupGet(r => r.Table).Returns(() => new List<BankStatementLine>().AsQueryable());
        repo.Setup(r => r.Insert(It.IsAny<IEnumerable<BankStatementLine>>()))
            .Throws(new Exception("An error occurred while saving the entity changes.", new Exception(databaseMessage)));
        var service = ServiceOver(harness, repo.Object);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            service.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n"), "t"));

        Assert.Equal(BankStatementImportService.AlreadyImportedMessage, ex.Message);
    }

    [Fact]
    public void Commit_OtherSaveFailuresAreNotReportedAsDuplicates()
    {
        using var harness = NewHarness(out var bank);
        var repo = new Mock<IRepository<BankStatementLine>>();
        repo.SetupGet(r => r.Table).Returns(() => new List<BankStatementLine>().AsQueryable());
        repo.Setup(r => r.Insert(It.IsAny<IEnumerable<BankStatementLine>>()))
            .Throws(new TimeoutException("database timed out"));
        var service = ServiceOver(harness, repo.Object);

        Assert.Throws<TimeoutException>(() => service.Commit(bank.Id, Csv("2026-10-01,10.00,A,,\n"), "t"));
    }
}

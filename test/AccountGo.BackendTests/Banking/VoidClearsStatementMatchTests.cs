// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.TaxSystem;
using Moq;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>
/// Bank transaction voids, bill payment voids, and invoice/receipt voids all reverse through
/// <see cref="FinancialService.CreateReversingEntry"/>; that is where statement matches are released.
/// </summary>
public class VoidClearsStatementMatchTests
{
    private static FinancialService NewFinancialService(BankingTestHarness harness, bool withStatementRepo = true) =>
        new(
            Mock.Of<IInventoryService>(),
            Mock.Of<ITaxService>(),
            harness.Repos.GlHeaders,
            harness.Repos.GlLines,
            harness.Repos.Accounts,
            Mock.Of<IRepository<AccountClass>>(),
            Mock.Of<IRepository<Tax>>(),
            Mock.Of<IRepository<JournalEntryHeader>>(),
            Mock.Of<IRepository<JournalEntryLine>>(),
            harness.Repos.FiscalYears,
            Mock.Of<IRepository<TaxGroup>>(),
            Mock.Of<IRepository<ItemTaxGroup>>(),
            harness.Repos.PaymentTerms,
            harness.Repos.Banks,
            Mock.Of<IRepository<Item>>(),
            harness.Repos.GlSettings,
            harness.Repos.Settings,
            bankReconciliationRepo: harness.Repos.Reconciliations,
            bankStatementLineRepo: withStatementRepo ? harness.Repos.StatementLines : null);

    private static (GeneralLedgerHeader Header, GeneralLedgerLine BankLine) PostTwoSided(
        BankingTestHarness harness, int bankAccountId, DocumentTypes documentType, decimal amount, DateTime date)
    {
        var header = new GeneralLedgerHeader
        {
            Id = harness.Repos.NextGlHeaderId(),
            CompanyId = 1,
            DocumentType = documentType,
            Date = date,
            Description = "seed"
        };
        var bankLine = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = header.Id,
            AccountId = bankAccountId,
            DrCr = DrOrCrSide.Cr,
            Amount = amount
        };
        var contraLine = new GeneralLedgerLine
        {
            Id = harness.Repos.NextGlLineId(),
            CompanyId = 1,
            GeneralLedgerHeaderId = header.Id,
            AccountId = harness.RequireAccount("50100").Id,
            DrCr = DrOrCrSide.Dr,
            Amount = amount
        };
        header.GeneralLedgerLines.Add(bankLine);
        header.GeneralLedgerLines.Add(contraLine);
        harness.Repos.GlHeaders.Insert(header);
        harness.Repos.GlLines.Insert(bankLine);
        harness.Repos.GlLines.Insert(contraLine);
        return (header, bankLine);
    }

    private static BankStatementLine AddMatchedStatementLine(BankingTestHarness harness, int bankId, GeneralLedgerLine matchedTo, decimal amount)
    {
        var statementLine = new BankStatementLine
        {
            CompanyId = 1,
            BankId = bankId,
            BankStatementImportId = 1,
            Source = BankStatementSources.Csv,
            ExternalId = Guid.NewGuid().ToString("N"),
            PostedDate = BankingTestHarness.Utc(2026, 10, 5),
            Amount = amount,
            MatchedGeneralLedgerLineId = matchedTo.Id,
            CreatedAt = DateTime.UtcNow
        };
        harness.Repos.StatementLines.Insert(statementLine);
        return statementLine;
    }

    private static BankingTestHarness NewHarness(out int bankId, out int bankAccountId)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        var bank = harness.CreateBank("Checking");
        bankId = bank.Id;
        bankAccountId = bank.AccountId!.Value;
        return harness;
    }

    [Theory]
    [InlineData(DocumentTypes.BankTransaction)]
    [InlineData(DocumentTypes.PurchaseInvoicePayment)]
    [InlineData(DocumentTypes.SalesReceipt)]
    public void Void_ReleasesStatementLineMatchedToTheVoidedEntry(DocumentTypes documentType)
    {
        using var harness = NewHarness(out var bankId, out var bankAccountId);
        var (header, bankLine) = PostTwoSided(harness, bankAccountId, documentType, 40m, BankingTestHarness.Utc(2026, 10, 5));
        var matched = AddMatchedStatementLine(harness, bankId, bankLine, -40m);
        var unrelatedLine = PostTwoSided(harness, bankAccountId, documentType, 15m, BankingTestHarness.Utc(2026, 10, 6)).BankLine;
        var unrelated = AddMatchedStatementLine(harness, bankId, unrelatedLine, -15m);

        var reversal = NewFinancialService(harness).CreateReversingEntry(header.Id, "Void", BankingTestHarness.Utc(2026, 10, 9));

        Assert.Equal(header.Id, reversal.ReversedGeneralLedgerHeaderId);
        Assert.Null(matched.MatchedGeneralLedgerLineId);
        Assert.Equal(unrelatedLine.Id, unrelated.MatchedGeneralLedgerLineId);
    }

    [Fact]
    public void Void_ClearsEveryStatementLineMatchedToTheEntry()
    {
        using var harness = NewHarness(out var bankId, out var bankAccountId);
        var (header, bankLine) = PostTwoSided(harness, bankAccountId, DocumentTypes.BankTransaction, 40m, BankingTestHarness.Utc(2026, 10, 5));
        var contraLine = header.GeneralLedgerLines.Single(l => l.Id != bankLine.Id);
        var first = AddMatchedStatementLine(harness, bankId, bankLine, -40m);
        var second = AddMatchedStatementLine(harness, bankId, contraLine, 40m);

        NewFinancialService(harness).CreateReversingEntry(header.Id, "Void", BankingTestHarness.Utc(2026, 10, 9));

        Assert.Null(first.MatchedGeneralLedgerLineId);
        Assert.Null(second.MatchedGeneralLedgerLineId);
    }

    [Fact]
    public void Void_RefusedBecauseOfDateKeepsTheMatch()
    {
        using var harness = NewHarness(out var bankId, out var bankAccountId);
        var (header, bankLine) = PostTwoSided(harness, bankAccountId, DocumentTypes.BankTransaction, 40m, BankingTestHarness.Utc(2026, 10, 5));
        var matched = AddMatchedStatementLine(harness, bankId, bankLine, -40m);

        Assert.Throws<InvalidOperationException>(() =>
            NewFinancialService(harness).CreateReversingEntry(header.Id, "Void", BankingTestHarness.Utc(2026, 10, 1)));

        Assert.Equal(bankLine.Id, matched.MatchedGeneralLedgerLineId);
    }

    [Fact]
    public void Void_WithoutStatementRepository_StillReverses()
    {
        using var harness = NewHarness(out _, out var bankAccountId);
        var (header, _) = PostTwoSided(harness, bankAccountId, DocumentTypes.BankTransaction, 40m, BankingTestHarness.Utc(2026, 10, 5));

        var reversal = NewFinancialService(harness, withStatementRepo: false)
            .CreateReversingEntry(header.Id, "Void", BankingTestHarness.Utc(2026, 10, 9));

        Assert.Equal(2, reversal.GeneralLedgerLines.Count);
    }
}

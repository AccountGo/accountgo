// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Dto.Banking;
using Moq;
using Services.Banking;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class ReconciledPeriodGuardTests
{
    private static DateTime D(int y, int m, int d) => BankingTestHarness.Utc(y, m, d);

    private static BankingTestHarness NewHarness(out BankAccountDto bank, DateTime? reconciledThrough = null)
    {
        var harness = new BankingTestHarness();
        harness.SeedCashTree();
        harness.SeedExpenseAccount();
        bank = harness.CreateBank("Checking");
        if (reconciledThrough.HasValue)
            AddReconciliation(harness, bank.Id, reconciledThrough.Value, BankReconciliationStatuses.Finished);
        return harness;
    }

    private static void AddReconciliation(BankingTestHarness harness, int bankId, DateTime end, BankReconciliationStatuses status)
    {
        harness.Repos.Reconciliations.Insert(new BankReconciliation
        {
            CompanyId = 1,
            BankId = bankId,
            StatementEndDate = end,
            Status = status,
            CreatedAt = end
        });
    }

    private static GeneralLedgerHeader Entry(int accountId, int contraId, DateTime date, int? reversalOf = null)
    {
        var header = new GeneralLedgerHeader
        {
            CompanyId = 1,
            DocumentType = DocumentTypes.JournalEntry,
            Date = date,
            Description = "test",
            ReversedGeneralLedgerHeaderId = reversalOf
        };
        header.GeneralLedgerLines.Add(new GeneralLedgerLine { DrCr = DrOrCrSide.Dr, AccountId = accountId, Amount = 10m });
        header.GeneralLedgerLines.Add(new GeneralLedgerLine { DrCr = DrOrCrSide.Cr, AccountId = contraId, Amount = 10m });
        return header;
    }

    private static CreateBankTransactionRequest MoneyOut(BankingTestHarness harness, int bankId, DateTime date) => new()
    {
        Kind = (int)BankTransactionKinds.MoneyOut,
        BankId = bankId,
        ContraAccountId = harness.RequireAccount("50100").Id,
        Date = date,
        Amount = 10m
    };

    [Fact]
    public void BankTransaction_OnOrBeforeReconciledThrough_IsBlockedWithoutConfirmation()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));

        var before = Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 15))));
        var onTheDay = Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 30))));

        Assert.Equal("Checking", before.BankName);
        Assert.Equal(D(2026, 9, 30), before.StatementEndDate);
        Assert.Equal(D(2026, 9, 15), before.PostingDate);
        Assert.Contains("2026-09-30", onTheDay.Message);
        Assert.Contains("Confirm", onTheDay.Message);
    }

    [Fact]
    public void BankTransaction_WithConfirmation_Posts()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        harness.Guard.Confirm();

        var created = harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 15)));

        Assert.True(harness.Guard.IsConfirmed);
        Assert.NotNull(created.GeneralLedgerHeaderId);
    }

    [Fact]
    public void BankTransaction_AfterReconciledThrough_PostsWithoutConfirmation()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));

        var created = harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 10, 1)));

        Assert.False(harness.Guard.IsConfirmed);
        Assert.NotNull(created.GeneralLedgerHeaderId);
    }

    [Fact]
    public void BlockedPosting_LeavesNothingInTheLedger()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        var linesBefore = harness.Repos.GlLines.Table.Count();

        Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 10))));

        Assert.Equal(linesBefore, harness.Repos.GlLines.Table.Count());
    }

    [Fact]
    public void Transfer_IsBlocked_WhenEitherBankIsReconciledPastTheDate()
    {
        using var harness = NewHarness(out var checking, reconciledThrough: null);
        var savings = harness.CreateBank("Savings");
        AddReconciliation(harness, savings.Id, D(2026, 9, 30), BankReconciliationStatuses.Finished);

        var ex = Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            harness.Transactions.Create(new CreateBankTransactionRequest
            {
                Kind = (int)BankTransactionKinds.Transfer,
                BankId = checking.Id,
                ToBankId = savings.Id,
                Date = D(2026, 9, 20),
                Amount = 10m
            }));

        Assert.Equal("Savings", ex.BankName);
    }

    [Fact]
    public void OnlyFinishedReconciliationsCount()
    {
        using var harness = NewHarness(out var bank);
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Draft);
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Undone);

        var created = harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 15)));

        Assert.NotNull(created.GeneralLedgerHeaderId);
    }

    [Fact]
    public void UsesTheLatestFinishedStatementEnd()
    {
        using var harness = NewHarness(out var bank, D(2026, 8, 31));
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Finished);

        Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            harness.Transactions.Create(MoneyOut(harness, bank.Id, D(2026, 9, 5))));
    }

    [Fact]
    public void EntryWithoutABankAccount_IsNotChecked()
    {
        using var harness = NewHarness(out _, D(2026, 9, 30));
        var expense = harness.RequireAccount("50100").Id;
        var equity = harness.RequireAccount("30700").Id;

        harness.Guard.EnsurePostingAllowed(Entry(expense, equity, D(2026, 9, 1)));
    }

    [Fact]
    public void ReversingEntry_IsNotChecked()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        var expense = harness.RequireAccount("50100").Id;

        harness.Guard.EnsurePostingAllowed(Entry(bank.AccountId!.Value, expense, D(2026, 9, 1), reversalOf: 5));
    }

    [Fact]
    public void PlainCashAccountsWithoutABankRecord_AreNotChecked()
    {
        using var harness = NewHarness(out _, D(2026, 9, 30));
        var cashChild = harness.RequireAccount("10112").Id;
        var expense = harness.RequireAccount("50100").Id;

        harness.Guard.EnsurePostingAllowed(Entry(cashChild, expense, D(2026, 9, 1)));
    }

    [Fact]
    public void OtherTenantsReconciliation_DoesNotBlock()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        harness.Repos.Reconciliations.Table.Single().CompanyId = 2;
        var expense = harness.RequireAccount("50100").Id;

        harness.Guard.EnsurePostingAllowed(Entry(bank.AccountId!.Value, expense, D(2026, 9, 1)));
    }

    [Fact]
    public void JournalReceiptAndBillPayment_ShareTheFinancialServiceChokePoint()
    {
        // Journal posting, sales receipts, and bill payments all validate their GL header through
        // FinancialService.ValidateGeneralLedgerEntry; the bank-transaction path goes through SaveGeneralLedgerEntry.
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        var expense = harness.RequireAccount("50100");
        var financial = BuildFinancialService(harness);

        var blocked = Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            financial.ValidateGeneralLedgerEntry(Entry(bank.AccountId!.Value, expense.Id, D(2026, 9, 12))));
        Assert.Equal("Checking", blocked.BankName);

        Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() =>
            financial.SaveGeneralLedgerEntry(Entry(bank.AccountId!.Value, expense.Id, D(2026, 9, 12))));

        Assert.True(financial.ValidateGeneralLedgerEntry(Entry(bank.AccountId!.Value, expense.Id, D(2026, 10, 2))));

        harness.Guard.Confirm();
        Assert.True(financial.ValidateGeneralLedgerEntry(Entry(bank.AccountId!.Value, expense.Id, D(2026, 9, 12))));
    }

    private static UpdateBankAccountRequest OpeningBalanceUpdate(BankAccountDto bank, decimal amount, DateTime asOf) => new()
    {
        Name = bank.Name,
        Type = 1,
        OpeningBalance = amount,
        OpeningBalanceDate = asOf
    };

    [Fact]
    public void OpeningBalance_SetAfterAFinishedReconciliation_NeedsNoConfirmation()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));

        var updated = harness.Banks.Update(bank.Id, OpeningBalanceUpdate(bank, 500m, D(2026, 9, 1)), includeFullNumbers: false);

        Assert.Equal(500m, updated.OpeningBalance);
        Assert.False(harness.Guard.IsConfirmed);
        var openingTx = Assert.Single(harness.Repos.BankTransactions.Table, t => t.Kind == BankTransactionKinds.OpeningBalance);
        Assert.NotNull(openingTx.GeneralLedgerHeaderId);
    }

    [Fact]
    public void OpeningBalance_ChangedAfterAFinishedReconciliation_NeedsNoConfirmation()
    {
        using var harness = NewHarness(out var bank, reconciledThrough: null);
        harness.Banks.Update(bank.Id, OpeningBalanceUpdate(bank, 500m, D(2026, 9, 1)), includeFullNumbers: false);
        AddReconciliation(harness, bank.Id, D(2026, 9, 30), BankReconciliationStatuses.Finished);

        var updated = harness.Banks.Update(bank.Id, OpeningBalanceUpdate(bank, 650m, D(2026, 9, 1)), includeFullNumbers: false);

        Assert.Equal(650m, updated.OpeningBalance);
        Assert.False(harness.Guard.IsConfirmed);
    }

    [Fact]
    public void OpeningBalanceExemption_DoesNotCoverOtherBankTransactionEntries()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        var expense = harness.RequireAccount("50100").Id;
        var moneyOut = harness.Repos.BankTransactions;
        moneyOut.Insert(new BankTransaction
        {
            CompanyId = 1,
            BankId = bank.Id,
            Kind = BankTransactionKinds.MoneyOut,
            Date = D(2026, 9, 10),
            Amount = 10m
        });
        var entry = Entry(bank.AccountId!.Value, expense, D(2026, 9, 10));
        entry.DocumentType = DocumentTypes.BankTransaction;
        entry.SourceDocumentId = moneyOut.Table.Single().Id;

        Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() => harness.Guard.EnsurePostingAllowed(entry));
    }

    [Fact]
    public void OpeningBalanceExemption_IgnoresAnotherCompanysOpeningBalanceTransaction()
    {
        using var harness = NewHarness(out var bank, D(2026, 9, 30));
        var expense = harness.RequireAccount("50100").Id;
        harness.Repos.BankTransactions.Insert(new BankTransaction
        {
            CompanyId = 2,
            BankId = bank.Id,
            Kind = BankTransactionKinds.OpeningBalance,
            Date = D(2026, 9, 10),
            Amount = 10m
        });
        var entry = Entry(bank.AccountId!.Value, expense, D(2026, 9, 10));
        entry.DocumentType = DocumentTypes.BankTransaction;
        entry.SourceDocumentId = harness.Repos.BankTransactions.Table.Single().Id;

        Assert.Throws<ReconciledPeriodConfirmationRequiredException>(() => harness.Guard.EnsurePostingAllowed(entry));
    }

    private static FinancialService BuildFinancialService(BankingTestHarness harness) =>
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
            Mock.Of<IRepository<PaymentTerm>>(),
            harness.Repos.Banks,
            Mock.Of<IRepository<Item>>(),
            Mock.Of<IRepository<GeneralLedgerSetting>>(),
            harness.Repos.Settings,
            bankReconciliationRepo: harness.Repos.Reconciliations,
            reconciledPeriodGuard: harness.Guard);
}

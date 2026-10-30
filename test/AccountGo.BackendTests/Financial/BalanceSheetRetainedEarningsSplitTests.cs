// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Globalization;
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

namespace AccountGo.BackendTests.Financial;

/// <summary>Phase 2a of #455: balance sheet splits retained earnings and profit for the year.</summary>
public class BalanceSheetRetainedEarningsSplitTests
{
    private static DateTime D(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Harness : IDisposable
    {
        public Banking.BankingMemoryRepo<CompanySetting> Settings { get; } = new();
        public Banking.BankingMemoryRepo<FinancialYear> FiscalYears { get; } = new();
        public Banking.BankingMemoryRepo<Account> Accounts { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
        public FinancialService Financial { get; }

        public int CashId { get; }
        public int EquityId { get; }
        public int RevenueId { get; }
        public int ExpenseId { get; }

        public Harness(FiscalYearPattern pattern = FiscalYearPattern.Calendar, int? startMonth = null)
        {
            Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                BooksStartDate = D(2025, 1, 1),
                FiscalYearPattern = pattern,
                FiscalYearStartMonth = startMonth,
                Currency = "USD"
            });

            CashId = 10;
            EquityId = 20;
            RevenueId = 40;
            ExpenseId = 50;
            Accounts.Insert(new Account { Id = CashId, CompanyId = 1, AccountCode = "10111", AccountName = "Cash", AccountClassId = 1, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = EquityId, CompanyId = 1, AccountCode = "30100", AccountName = "Equity", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = RevenueId, CompanyId = 1, AccountCode = "40100", AccountName = "Sales", AccountClassId = 4, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = ExpenseId, CompanyId = 1, AccountCode = "50100", AccountName = "Expense", AccountClassId = 5, DrOrCrSide = DrOrCrSide.Dr });

            Financial = new FinancialService(
                Mock.Of<IInventoryService>(),
                Mock.Of<ITaxService>(),
                GlHeaders,
                GlLines,
                Accounts,
                Mock.Of<IRepository<AccountClass>>(),
                Mock.Of<IRepository<Tax>>(),
                Mock.Of<IRepository<JournalEntryHeader>>(),
                Mock.Of<IRepository<JournalEntryLine>>(),
                FiscalYears,
                Mock.Of<IRepository<TaxGroup>>(),
                Mock.Of<IRepository<ItemTaxGroup>>(),
                Mock.Of<IRepository<PaymentTerm>>(),
                Mock.Of<IRepository<Bank>>(),
                Mock.Of<IRepository<Item>>(),
                Mock.Of<IRepository<GeneralLedgerSetting>>(),
                Settings);
        }

        public void Dispose() { }

        public void Post(DateTime date, int debitAccountId, int creditAccountId, decimal amount)
        {
            var header = new GeneralLedgerHeader
            {
                Id = GlHeaders.Table.Count() + 1,
                CompanyId = 1,
                Date = date,
                DocumentType = DocumentTypes.JournalEntry,
                Description = "test"
            };
            var debit = new GeneralLedgerLine
            {
                Id = GlLines.Table.Count() + 1,
                CompanyId = 1,
                AccountId = debitAccountId,
                Account = Accounts.Table.Single(a => a.Id == debitAccountId),
                DrCr = DrOrCrSide.Dr,
                Amount = amount,
                GeneralLedgerHeaderId = header.Id,
                GeneralLedgerHeader = header
            };
            var credit = new GeneralLedgerLine
            {
                Id = GlLines.Table.Count() + 2,
                CompanyId = 1,
                AccountId = creditAccountId,
                Account = Accounts.Table.Single(a => a.Id == creditAccountId),
                DrCr = DrOrCrSide.Cr,
                Amount = amount,
                GeneralLedgerHeaderId = header.Id,
                GeneralLedgerHeader = header
            };
            header.GeneralLedgerLines.Add(debit);
            header.GeneralLedgerLines.Add(credit);
            GlLines.Insert(debit);
            GlLines.Insert(credit);
            GlHeaders.Insert(header);
        }
    }

    [Fact]
    public void BalanceSheet_On2027Jan15_SplitsPriorYearAndJanuaryProfit_Calendar()
    {
        using var harness = new Harness();
        // FY2026 profit: sales 1000 − expense 400 = 600
        harness.Post(D(2026, 6, 15), harness.CashId, harness.RevenueId, 1000m);
        harness.Post(D(2026, 7, 1), harness.ExpenseId, harness.CashId, 400m);
        // January 2027 profit: sales 200 − expense 50 = 150
        harness.Post(D(2027, 1, 10), harness.CashId, harness.RevenueId, 200m);
        harness.Post(D(2027, 1, 12), harness.ExpenseId, harness.CashId, 50m);

        var asOf = D(2027, 1, 15);
        var sheet = harness.Financial.BalanceSheet(to: asOf).ToList();
        var opening = Assert.Single(sheet, r => r.AccountName == "Retained earnings (opening)");
        var profit = Assert.Single(sheet, r => r.AccountName == "Profit for the year");
        Assert.Equal(600m, opening.Amount);
        Assert.Equal(150m, profit.Amount);
        Assert.DoesNotContain(sheet, r => r.AccountName.Contains("unclosed", StringComparison.OrdinalIgnoreCase));

        var assets = sheet.Where(r => r.AccountClassId == 1).Sum(r => r.Amount);
        var liabilitiesAndEquity = sheet.Where(r => r.AccountClassId == 2 || r.AccountClassId == 3).Sum(r => r.Amount);
        Assert.Equal(assets, liabilitiesAndEquity);
    }

    [Fact]
    public void BalanceSheet_AprilMarch_UsesFiscalYearContainingAsOf()
    {
        using var harness = new Harness(FiscalYearPattern.AprMar);
        // Prior FY (2025-04-01..2026-03-31): net 300
        harness.Post(D(2025, 8, 1), harness.CashId, harness.RevenueId, 300m);
        // Current FY starting 2026-04-01: net 80 through 2026-05-15
        harness.Post(D(2026, 4, 20), harness.CashId, harness.RevenueId, 100m);
        harness.Post(D(2026, 5, 1), harness.ExpenseId, harness.CashId, 20m);

        var asOf = DateTime.ParseExact("2026-05-15", "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        var sheet = harness.Financial.BalanceSheet(to: asOf).ToList();
        Assert.Equal(300m, Assert.Single(sheet, r => r.AccountName == "Retained earnings (opening)").Amount);
        Assert.Equal(80m, Assert.Single(sheet, r => r.AccountName == "Profit for the year").Amount);

        var assets = sheet.Where(r => r.AccountClassId == 1).Sum(r => r.Amount);
        var liabilitiesAndEquity = sheet.Where(r => r.AccountClassId == 2 || r.AccountClassId == 3).Sum(r => r.Amount);
        Assert.Equal(assets, liabilitiesAndEquity);
    }

    [Fact]
    public void BalanceSheet_OmitsZeroSplitLines()
    {
        using var harness = new Harness();
        harness.Post(D(2027, 2, 1), harness.CashId, harness.RevenueId, 50m);

        var sheet = harness.Financial.BalanceSheet(to: D(2027, 2, 15)).ToList();
        Assert.DoesNotContain(sheet, r => r.AccountName == "Retained earnings (opening)");
        Assert.Equal(50m, Assert.Single(sheet, r => r.AccountName == "Profit for the year").Amount);
    }

    [Fact]
    public void BalanceSheet_IgnoresDeactivatedOverlappingCalendarYear_UsesActiveAprilMarchStart()
    {
        using var harness = new Harness(FiscalYearPattern.AprMar);
        // Stale calendar year left after a pattern change must never drive the split.
        harness.FiscalYears.Insert(new FinancialYear
        {
            Id = 1,
            CompanyId = 1,
            FiscalYearCode = "FY2026",
            FiscalYearName = "Calendar 2026",
            StartDate = D(2026, 1, 1),
            EndDate = D(2026, 12, 31),
            IsActive = false
        });
        harness.FiscalYears.Insert(new FinancialYear
        {
            Id = 2,
            CompanyId = 1,
            FiscalYearCode = "FY2026-27",
            FiscalYearName = "Apr–Mar 2026-27",
            StartDate = D(2026, 4, 1),
            EndDate = D(2027, 3, 31),
            IsActive = true
        });

        // Pre–April → opening retained earnings; post–April → profit for the year
        harness.Post(D(2026, 2, 1), harness.CashId, harness.RevenueId, 100m);
        harness.Post(D(2026, 4, 15), harness.CashId, harness.RevenueId, 50m);

        var sheet = harness.Financial.BalanceSheet(to: D(2026, 5, 15)).ToList();
        Assert.Equal(100m, Assert.Single(sheet, r => r.AccountName == "Retained earnings (opening)").Amount);
        Assert.Equal(50m, Assert.Single(sheet, r => r.AccountName == "Profit for the year").Amount);

        var assets = sheet.Where(r => r.AccountClassId == 1).Sum(r => r.Amount);
        var liabilitiesAndEquity = sheet.Where(r => r.AccountClassId == 2 || r.AccountClassId == 3).Sum(r => r.Amount);
        Assert.Equal(assets, liabilitiesAndEquity);
    }
}

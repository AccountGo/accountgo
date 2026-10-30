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

/// <summary>Phase 1 of #455: lazily create open fiscal years so posting continues past year end.</summary>
public class FiscalYearRolloverTests
{
    private static DateTime D(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Parse(string iso) =>
        DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed class Harness : IDisposable
    {
        public Banking.BankingMemoryRepo<CompanySetting> Settings { get; } = new();
        public Banking.BankingMemoryRepo<FinancialYear> FiscalYears { get; } = new();
        public Banking.BankingMemoryRepo<Account> Accounts { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
        public FinancialService Financial { get; }

        public Harness(
            DateTime booksStart,
            FiscalYearPattern pattern = FiscalYearPattern.Calendar,
            int? startMonth = null,
            params FinancialYear[] seedYears)
        {
            Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                BooksStartDate = booksStart,
                FiscalYearPattern = pattern,
                FiscalYearStartMonth = startMonth,
                Currency = "USD"
            });
            foreach (var year in seedYears)
                FiscalYears.Insert(year);

            var cash = new Account
            {
                Id = 10,
                CompanyId = 1,
                AccountCode = "10111",
                AccountName = "Cash",
                AccountClassId = 1,
                DrOrCrSide = DrOrCrSide.Dr
            };
            var equity = new Account
            {
                Id = 20,
                CompanyId = 1,
                AccountCode = "30100",
                AccountName = "Equity",
                AccountClassId = 3,
                DrOrCrSide = DrOrCrSide.Cr
            };
            Accounts.Insert(cash);
            Accounts.Insert(equity);

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

        public GeneralLedgerHeader BalancedEntry(DateTime date)
        {
            var header = Financial.CreateGeneralLedgerHeader(DocumentTypes.JournalEntry, date, "test");
            header.GeneralLedgerLines.Add(Financial.CreateGeneralLedgerLine(DrOrCrSide.Dr, 10, 10m));
            header.GeneralLedgerLines.Add(Financial.CreateGeneralLedgerLine(DrOrCrSide.Cr, 20, 10m));
            return header;
        }
    }

    private static FinancialYear Year(string code, DateTime start, DateTime end, bool active = true) => new()
    {
        CompanyId = 1,
        FiscalYearCode = code,
        FiscalYearName = code,
        StartDate = start,
        EndDate = end,
        IsActive = active
    };

    [Fact]
    public void CalendarWorkspace_PostsAcrossYearEnd_CreatesFy2027Once()
    {
        using var harness = new Harness(
            D(2026, 1, 1),
            seedYears: Year("FY2026", D(2026, 1, 1), Parse("2026-12-31").Date.AddDays(1).AddTicks(-1)));

        Assert.True(harness.Financial.ValidateGeneralLedgerEntry(harness.BalancedEntry(D(2026, 12, 31))));
        Assert.True(harness.Financial.ValidateGeneralLedgerEntry(harness.BalancedEntry(D(2027, 1, 1))));
        Assert.Equal(1, harness.FiscalYears.Table.Count(y => y.FiscalYearCode == "FY2027"));

        Assert.True(harness.Financial.ValidateGeneralLedgerEntry(harness.BalancedEntry(D(2027, 1, 15))));
        Assert.Equal(1, harness.FiscalYears.Table.Count(y => y.FiscalYearCode == "FY2027"));

        var fy2027 = harness.FiscalYears.Table.Single(y => y.FiscalYearCode == "FY2027");
        Assert.Equal(D(2027, 1, 1), fy2027.StartDate);
        Assert.True(fy2027.IsActive);
    }

    [Fact]
    public void AprilMarchWorkspace_PostingOnAprilFirst_CreatesYearStartingApril()
    {
        using var harness = new Harness(
            D(2026, 4, 1),
            FiscalYearPattern.AprMar,
            seedYears: Year("FY2026-27", D(2026, 4, 1), Parse("2027-03-31").Date.AddDays(1).AddTicks(-1)));

        Assert.True(harness.Financial.ValidateGeneralLedgerEntry(harness.BalancedEntry(D(2027, 4, 1))));

        var created = harness.FiscalYears.Table.Single(y => y.FiscalYearCode == "FY2027-28");
        Assert.Equal(D(2027, 4, 1), created.StartDate);
        Assert.True(created.IsActive);
    }

    [Fact]
    public void MidYearBooksStart_PostingInNextCalendarYear_CreatesMissingYear()
    {
        using var harness = new Harness(
            D(2024, 3, 1),
            seedYears: Year("FY2024", D(2024, 1, 1), Parse("2024-12-31").Date.AddDays(1).AddTicks(-1)));

        Assert.True(harness.Financial.ValidateGeneralLedgerEntry(harness.BalancedEntry(D(2025, 6, 30))));
        Assert.Contains(harness.FiscalYears.Table, y => y.FiscalYearCode == "FY2025");
    }

    [Fact]
    public void PostingBeforeBooksStart_StillFailsWithBooksStartMessage()
    {
        using var harness = new Harness(
            D(2026, 4, 1),
            seedYears: Year("FY2026", D(2026, 1, 1), Parse("2026-12-31").Date.AddDays(1).AddTicks(-1)));

        var ex = Assert.Throws<BooksStartViolationException>(() =>
            harness.Financial.CreateGeneralLedgerHeader(DocumentTypes.JournalEntry, D(2026, 3, 31), "early"));
        Assert.Contains("books start", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(harness.FiscalYears.Table, y => y.FiscalYearCode == "FY2025");
    }

    [Fact]
    public void EnsureFiscalYearsThrough_DoesNotReactivateInactiveYear()
    {
        using var harness = new Harness(
            D(2025, 1, 1),
            seedYears:
            [
                Year("FY2025", D(2025, 1, 1), Parse("2025-12-31").Date.AddDays(1).AddTicks(-1), active: false),
                Year("FY2026", D(2026, 1, 1), Parse("2026-12-31").Date.AddDays(1).AddTicks(-1))
            ]);

        harness.Financial.EnsureFiscalYearsThrough(D(2025, 6, 1));

        Assert.False(harness.FiscalYears.Table.Single(y => y.FiscalYearCode == "FY2025").IsActive);
        var covered = harness.FiscalYears.Table.FirstOrDefault(fy =>
            fy.IsActive && D(2025, 6, 1) >= fy.StartDate && D(2025, 6, 1) <= fy.EndDate);
        Assert.Null(covered);
    }
}

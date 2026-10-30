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
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Financial;

/// <summary>Phase 2a of #455: books lock hard-stops postings when Financials is enabled.</summary>
public class BooksLockTests
{
    private static DateTime D(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Parse(string iso) =>
        DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed class Harness : IDisposable
    {
        public Banking.BankingMemoryRepo<CompanySetting> Settings { get; } = new();
        public Banking.BankingMemoryRepo<FinancialYear> FiscalYears { get; } = new();
        public Banking.BankingMemoryRepo<Account> Accounts { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
        public Mock<IEntitlementService> Entitlements { get; } = new();
        public Mock<ITenantContext> Tenant { get; } = new();
        public BooksLockGuard Guard { get; }
        public FinancialService Financial { get; }

        public Harness(DateTime? lockedThrough, bool financialsAllowed = true)
        {
            Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                BooksStartDate = D(2025, 1, 1),
                BooksLockedThrough = lockedThrough,
                FiscalYearPattern = FiscalYearPattern.Calendar,
                Currency = "USD"
            });
            FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "FY2026",
                FiscalYearName = "FY2026",
                StartDate = D(2026, 1, 1),
                EndDate = D(2026, 12, 31).AddDays(1).AddTicks(-1),
                IsActive = true
            });
            FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "FY2027",
                FiscalYearName = "FY2027",
                StartDate = D(2027, 1, 1),
                EndDate = D(2027, 12, 31).AddDays(1).AddTicks(-1),
                IsActive = true
            });
            Accounts.Insert(new Account { Id = 10, CompanyId = 1, AccountCode = "10111", AccountName = "Cash", AccountClassId = 1, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = 20, CompanyId = 1, AccountCode = "30100", AccountName = "Equity", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });

            Tenant.Setup(t => t.IsResolved).Returns(true);
            Tenant.Setup(t => t.CompanyId).Returns(1);
            Tenant.Setup(t => t.RequireCompanyId()).Returns(1);
            Entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
                .Returns((int _, EntitlementAction action) =>
                {
                    var module = Assert.IsType<EntitlementAction.Module>(action);
                    var allowed = financialsAllowed || !module.AnyOf.Contains(SystemModules.Financials);
                    return allowed
                        ? new EntitlementDecision(true, null, null)
                        : new EntitlementDecision(false, "MODULE_NOT_SUBSCRIBED", "denied");
                });

            Guard = new BooksLockGuard(Settings, Entitlements.Object, Tenant.Object);
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
                Settings,
                booksLockGuard: Guard);
        }

        public void Dispose() { }

        public GeneralLedgerHeader Entry(DateTime date)
        {
            var header = new GeneralLedgerHeader
            {
                CompanyId = 1,
                Date = date,
                DocumentType = DocumentTypes.JournalEntry,
                Description = "test"
            };
            header.GeneralLedgerLines.Add(new GeneralLedgerLine { AccountId = 10, DrCr = DrOrCrSide.Dr, Amount = 10m });
            header.GeneralLedgerLines.Add(new GeneralLedgerLine { AccountId = 20, DrCr = DrOrCrSide.Cr, Amount = 10m });
            return header;
        }
    }

    [Fact]
    public void Guard_And_Validate_Refuse_OnOrBeforeLock_WhenFinancialsEnabled()
    {
        var lockDate = Parse("2026-12-31");
        using var harness = new Harness(lockDate);
        var expected = BooksLockedException.FormatMessage(lockDate);

        var onLock = Assert.Throws<BooksLockedException>(() => harness.Guard.EnsureDateAllowed(lockDate));
        Assert.Equal(expected, onLock.Message);
        var before = Assert.Throws<BooksLockedException>(() =>
            harness.Financial.ValidateGeneralLedgerEntry(harness.Entry(Parse("2026-06-15"))));
        Assert.Equal(expected, before.Message);

        harness.Financial.ValidateGeneralLedgerEntry(harness.Entry(Parse("2027-01-01")));
    }

    [Fact]
    public void InvoicingOnly_WithLockSetInDb_IsNotBlocked()
    {
        using var harness = new Harness(Parse("2026-12-31"), financialsAllowed: false);
        harness.Guard.EnsureDateAllowed(Parse("2026-06-15"));
        harness.Financial.ValidateGeneralLedgerEntry(harness.Entry(Parse("2026-06-15")));
    }

    [Fact]
    public void NoLock_AllowsAnyDate()
    {
        using var harness = new Harness(null);
        harness.Guard.EnsureDateAllowed(Parse("2026-06-15"));
        harness.Financial.ValidateGeneralLedgerEntry(harness.Entry(Parse("2026-06-15")));
    }
}

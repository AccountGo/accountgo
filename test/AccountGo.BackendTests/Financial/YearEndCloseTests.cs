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
using Core.Domain.Purchases;
using Core.Domain.Sales;
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

/// <summary>Phase 2b-1 of #455: year-end close service (in-memory harness).</summary>
public class YearEndCloseTests
{
    private static DateTime D(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime EndOfDay(DateTime date) =>
        date.Date.AddDays(1).AddTicks(-1);

    private static decimal NetDr(TrialBalance row) => row.Debit - row.Credit;

    /// <summary>Memory repos do not cascade GL line inserts (unlike EF SaveChanges).</summary>
    private sealed class GlHeaderRepoWithLines : IRepository<GeneralLedgerHeader>
    {
        private readonly Banking.BankingMemoryRepo<GeneralLedgerHeader> _inner = new();
        private readonly Banking.BankingMemoryRepo<GeneralLedgerLine> _lines;
        private readonly Banking.BankingMemoryRepo<Account> _accounts;

        public GlHeaderRepoWithLines(
            Banking.BankingMemoryRepo<GeneralLedgerLine> lines,
            Banking.BankingMemoryRepo<Account> accounts)
        {
            _lines = lines;
            _accounts = accounts;
        }

        public IQueryable<GeneralLedgerHeader> Table => _inner.Table;

        public GeneralLedgerHeader GetById(object id) => _inner.GetById(id);

        public void Insert(GeneralLedgerHeader entity)
        {
            _inner.Insert(entity);
            foreach (var line in entity.GeneralLedgerLines)
            {
                line.GeneralLedgerHeaderId = entity.Id;
                line.GeneralLedgerHeader = entity;
                line.Account ??= _accounts.Table.First(a => a.Id == line.AccountId);
                if (!_lines.Table.Contains(line))
                    _lines.Insert(line);
            }
        }

        public System.Threading.Tasks.Task InsertAsync(GeneralLedgerHeader entity)
        {
            Insert(entity);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Update(GeneralLedgerHeader entity) => _inner.Update(entity);

        public System.Threading.Tasks.Task UpdateAsync(GeneralLedgerHeader entity) => _inner.UpdateAsync(entity);

        public void Delete(GeneralLedgerHeader entity) => _inner.Delete(entity);

        public System.Threading.Tasks.Task DeleteAsync(GeneralLedgerHeader entity) => _inner.DeleteAsync(entity);

        public IQueryable<GeneralLedgerHeader> GetAllIncluding(params System.Linq.Expressions.Expression<Func<GeneralLedgerHeader, object>>[] includeProperties) =>
            _inner.GetAllIncluding(includeProperties);
    }

    /// <summary>Memory repos do not cascade journal posting into GL tables (unlike EF SaveChanges).</summary>
    private sealed class JournalRepoWithGlPersist : IRepository<JournalEntryHeader>
    {
        private readonly Banking.BankingMemoryRepo<JournalEntryHeader> _inner = new();
        private readonly GlHeaderRepoWithLines _headers;
        private readonly Banking.BankingMemoryRepo<GeneralLedgerLine> _lines;
        private readonly Banking.BankingMemoryRepo<Account> _accounts;

        public JournalRepoWithGlPersist(
            GlHeaderRepoWithLines headers,
            Banking.BankingMemoryRepo<GeneralLedgerLine> lines,
            Banking.BankingMemoryRepo<Account> accounts)
        {
            _headers = headers;
            _lines = lines;
            _accounts = accounts;
        }

        public IQueryable<JournalEntryHeader> Table => _inner.Table;

        public JournalEntryHeader GetById(object id) => _inner.GetById(id);

        public void Insert(JournalEntryHeader entity) => _inner.Insert(entity);

        public System.Threading.Tasks.Task InsertAsync(JournalEntryHeader entity) => _inner.InsertAsync(entity);

        public void Update(JournalEntryHeader entity)
        {
            if (entity.GeneralLedgerHeader != null && !entity.GeneralLedgerHeaderId.HasValue)
            {
                var gl = entity.GeneralLedgerHeader;
                if (gl.Id == 0)
                    _headers.Insert(gl);
                entity.GeneralLedgerHeaderId = gl.Id;
                gl.CompanyId = entity.CompanyId;
                foreach (var line in gl.GeneralLedgerLines)
                {
                    line.CompanyId = entity.CompanyId;
                    line.GeneralLedgerHeaderId = gl.Id;
                    line.GeneralLedgerHeader = gl;
                    line.Account ??= _accounts.Table.First(a => a.Id == line.AccountId);
                    if (!_lines.Table.Contains(line))
                        _lines.Insert(line);
                }
            }

            _inner.Update(entity);
        }

        public System.Threading.Tasks.Task UpdateAsync(JournalEntryHeader entity)
        {
            Update(entity);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Delete(JournalEntryHeader entity) => _inner.Delete(entity);

        public System.Threading.Tasks.Task DeleteAsync(JournalEntryHeader entity) => _inner.DeleteAsync(entity);

        public IQueryable<JournalEntryHeader> GetAllIncluding(params System.Linq.Expressions.Expression<Func<JournalEntryHeader, object>>[] includeProperties) =>
            _inner.GetAllIncluding(includeProperties);
    }

    private sealed class Harness : IDisposable
    {
        public Banking.BankingMemoryRepo<CompanySetting> Settings { get; } = new();
        public Banking.BankingMemoryRepo<FinancialYear> FiscalYears { get; } = new();
        public Banking.BankingMemoryRepo<Account> Accounts { get; } = new();
        public GlHeaderRepoWithLines GlHeaders { get; }
        public Banking.BankingMemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
        public Banking.BankingMemoryRepo<GeneralLedgerSetting> GlSettings { get; } = new();
        public JournalRepoWithGlPersist Journals { get; }
        public Banking.BankingMemoryRepo<JournalEntryLine> JournalLines { get; } = new();
        public Banking.BankingMemoryRepo<SalesInvoiceHeader> SalesInvoices { get; } = new();
        public Banking.BankingMemoryRepo<PurchaseInvoiceHeader> PurchaseInvoices { get; } = new();
        public Banking.BankingMemoryRepo<Bank> Banks { get; } = new();
        public Banking.BankingMemoryRepo<BankReconciliation> Reconciliations { get; } = new();
        public Banking.BankingMemoryRepo<BankStatementLine> StatementLines { get; } = new();
        public Banking.BankingMemoryRepo<BooksLockChange> LockChanges { get; } = new();

        public Mock<IEntitlementService> Entitlements { get; } = new();
        public Mock<ITenantContext> Tenant { get; } = new();
        public BooksLockGuard Guard { get; }
        public FinancialService Financial { get; }
        public YearEndCloseService YearEnd { get; }

        public int CashId { get; }
        public int OwnerCapitalId { get; }
        public int RetainedId { get; }
        public int DrawingsId { get; }
        public int DividendsId { get; }
        public int RevenueId { get; }
        public int ExpenseId { get; }
        public int ObeId { get; }

        public DateTime Fy2025Start { get; } = D(2025, 1, 1);
        public DateTime Fy2025End { get; } = EndOfDay(D(2025, 12, 31));
        public const string Fy2025Code = "FY2025";

        public Harness(
            EntityType entityType = EntityType.SoleProprietor,
            DateTime? booksLockedThrough = null,
            DateTime? booksStartDate = null)
        {
            var permanentCode = entityType == EntityType.Corporation ? "30300" : "30100";
            var booksStart = booksStartDate ?? D(2025, 1, 1);
            GlHeaders = new GlHeaderRepoWithLines(GlLines, Accounts);
            Journals = new JournalRepoWithGlPersist(GlHeaders, GlLines, Accounts);

            Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                BooksStartDate = booksStart,
                BooksLockedThrough = booksLockedThrough,
                FiscalYearPattern = FiscalYearPattern.Calendar,
                Currency = "USD",
                EntityType = entityType
            });

            FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = Fy2025Code,
                FiscalYearName = Fy2025Code,
                StartDate = Fy2025Start,
                EndDate = Fy2025End,
                IsActive = true
            });
            FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "FY2026",
                FiscalYearName = "FY2026",
                StartDate = D(2026, 1, 1),
                EndDate = EndOfDay(D(2026, 12, 31)),
                IsActive = true
            });

            CashId = 10;
            OwnerCapitalId = 20;
            RetainedId = 30;
            DrawingsId = 35;
            DividendsId = 36;
            ObeId = 37;
            RevenueId = 40;
            ExpenseId = 50;

            Accounts.Insert(new Account { Id = CashId, CompanyId = 1, AccountCode = "10111", AccountName = "Cash", AccountClassId = 1, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = OwnerCapitalId, CompanyId = 1, AccountCode = "30100", AccountName = "Owner's Capital", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = RetainedId, CompanyId = 1, AccountCode = "30300", AccountName = "Retained Earnings", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = DrawingsId, CompanyId = 1, AccountCode = "30800", AccountName = "Owner's Drawings", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = DividendsId, CompanyId = 1, AccountCode = "30810", AccountName = "Dividends Declared", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = ObeId, CompanyId = 1, AccountCode = "30700", AccountName = "Opening Balance Equity", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = RevenueId, CompanyId = 1, AccountCode = "40100", AccountName = "Sales", AccountClassId = 4, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = ExpenseId, CompanyId = 1, AccountCode = "50100", AccountName = "Expense", AccountClassId = 5, DrOrCrSide = DrOrCrSide.Dr });

            var permanentId = permanentCode == "30300" ? RetainedId : OwnerCapitalId;
            GlSettings.Insert(new GeneralLedgerSetting { CompanyId = 1, PermanentAccountId = permanentId });

            Tenant.Setup(t => t.IsResolved).Returns(true);
            Tenant.Setup(t => t.CompanyId).Returns(1);
            Tenant.Setup(t => t.RequireCompanyId()).Returns(1);
            Entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
                .Returns(new EntitlementDecision(true, null, null));

            Guard = new BooksLockGuard(Settings, Entitlements.Object, Tenant.Object);
            Financial = new FinancialService(
                Mock.Of<IInventoryService>(),
                Mock.Of<ITaxService>(),
                GlHeaders,
                GlLines,
                Accounts,
                Mock.Of<IRepository<AccountClass>>(),
                Mock.Of<IRepository<Tax>>(),
                Journals,
                JournalLines,
                FiscalYears,
                Mock.Of<IRepository<TaxGroup>>(),
                Mock.Of<IRepository<ItemTaxGroup>>(),
                Mock.Of<IRepository<PaymentTerm>>(),
                Mock.Of<IRepository<Bank>>(),
                Mock.Of<IRepository<Item>>(),
                GlSettings,
                Settings,
                booksLockGuard: Guard);

            YearEnd = new YearEndCloseService(
                Financial,
                Guard,
                Tenant.Object,
                FiscalYears,
                Settings,
                Accounts,
                GlSettings,
                GlLines,
                Journals,
                SalesInvoices,
                PurchaseInvoices,
                Banks,
                Reconciliations,
                StatementLines,
                LockChanges);
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
            GlHeaders.Insert(header);
        }

        public void AddActiveBankWithoutReconciliation()
        {
            Banks.Insert(new Bank
            {
                CompanyId = 1,
                Name = "Unreconciled",
                Type = BankTypes.CheckingAccount,
                AccountId = CashId,
                IsActive = true
            });
        }

        public decimal CapitalBalance(int accountId) =>
            Financial.TrialBalance(Fy2025Start, Fy2025End)
                .Where(r => r.AccountId == accountId)
                .Select(NetDr)
                .FirstOrDefault();
    }

    [Fact]
    public void Close_SoleProprietor_PreservesIncomeStatement_AndLocksBooks()
    {
        using var harness = new Harness();
        harness.Post(D(2025, 6, 15), harness.CashId, harness.RevenueId, 1000m);
        harness.Post(D(2025, 7, 1), harness.ExpenseId, harness.CashId, 400m);
        harness.Post(D(2025, 8, 1), harness.DrawingsId, harness.CashId, 150m);

        var incomeBefore = harness.Financial.IncomeStatement(harness.Fy2025Start, harness.Fy2025End).ToList();
        var capitalBefore = harness.CapitalBalance(harness.OwnerCapitalId);

        var preview = harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "tester");

        Assert.Equal(FiscalYearCloseStatus.Closed, preview.Status);
        var incomeAfter = harness.Financial.IncomeStatement(harness.Fy2025Start, harness.Fy2025End).ToList();
        Assert.Equal(incomeBefore.Single(i => i.AccountCode == "40100").Amount,
            incomeAfter.Single(i => i.AccountCode == "40100").Amount);
        Assert.Equal(incomeBefore.Single(i => i.AccountCode == "50100").Amount,
            incomeAfter.Single(i => i.AccountCode == "50100").Amount);

        var tb = harness.Financial.TrialBalance(harness.Fy2025Start, harness.Fy2025End);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "40100"))), 0m, 0.005m);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "50100"))), 0m, 0.005m);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "30800"))), 0m, 0.005m);

        var capitalAfter = harness.CapitalBalance(harness.OwnerCapitalId);
        Assert.Equal(450m, -(capitalAfter - capitalBefore));

        var setting = harness.Settings.Table.Single();
        Assert.Equal(D(2025, 12, 31), setting.BooksLockedThrough!.Value.Date);
        var change = Assert.Single(harness.LockChanges.Table);
        Assert.Equal("Closed " + Harness.Fy2025Code, change.Reason);
        Assert.Equal(D(2025, 12, 31), change.NewLockedThrough!.Value.Date);
    }

    [Fact]
    public void Close_Corporation_ZerosDividendsAndPostsProfitToRetainedEarnings()
    {
        using var harness = new Harness(EntityType.Corporation);
        harness.Post(D(2025, 5, 10), harness.CashId, harness.RevenueId, 2000m);
        harness.Post(D(2025, 6, 1), harness.ExpenseId, harness.CashId, 500m);
        harness.Post(D(2025, 9, 1), harness.DividendsId, harness.CashId, 200m);

        harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "corp");

        var tb = harness.Financial.TrialBalance(harness.Fy2025Start, harness.Fy2025End);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "40100"))), 0m, 0.005m);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "50100"))), 0m, 0.005m);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "30810"))), 0m, 0.005m);
        Assert.Equal(1300m, tb.Single(r => r.AccountCode == "30300").Credit - tb.Single(r => r.AccountCode == "30300").Debit);
    }

    [Fact]
    public void Close_Blocked_ByOpeningBalanceEquity_DraftInvoice_AndEarlierOpenYear()
    {
        using (var harness = new Harness())
        {
            harness.Post(D(2025, 3, 1), harness.CashId, harness.ObeId, 50m);
            var ex = Assert.Throws<YearEndCloseException>(() =>
                harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "x"));
            Assert.Contains("blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
            var preview = harness.YearEnd.Preview(Harness.Fy2025Code);
            Assert.Contains(preview.Blocking, b => b.Code == "opening_balance_equity");
        }

        using (var harness = new Harness())
        {
            harness.SalesInvoices.Insert(new SalesInvoiceHeader
            {
                CompanyId = 1,
                Date = D(2025, 4, 1),
                Status = SalesInvoiceStatus.Draft,
                No = "DRAFT-1"
            });
            Assert.Throws<YearEndCloseException>(() =>
                harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "x"));
            Assert.Contains(harness.YearEnd.Preview(Harness.Fy2025Code).Blocking, b => b.Code == "draft_invoices");
        }

        using (var harness = new Harness(booksStartDate: D(2024, 1, 1)))
        {
            harness.FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "FY2024",
                FiscalYearName = "FY2024",
                StartDate = D(2024, 1, 1),
                EndDate = EndOfDay(D(2024, 12, 31)),
                IsActive = true
            });
            Assert.Throws<YearEndCloseException>(() =>
                harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "x"));
            Assert.Contains(harness.YearEnd.Preview(Harness.Fy2025Code).Blocking, b => b.Code == "earlier_year_open");
        }
    }

    [Fact]
    public void Close_Blocked_WhenFiscalYearHasNotEnded()
    {
        using var harness = new Harness();
        var preview = harness.YearEnd.Preview("FY2026");
        Assert.Contains(preview.Blocking, b => b.Code == "fy_not_ended");
        Assert.Throws<YearEndCloseException>(() =>
            harness.YearEnd.Close("FY2026", acknowledgeWarnings: true, closedBy: "x"));
    }

    [Fact]
    public void Close_RequiresAcknowledgedWarnings()
    {
        using var harness = new Harness();
        harness.Post(D(2025, 6, 1), harness.CashId, harness.RevenueId, 100m);
        harness.AddActiveBankWithoutReconciliation();

        Assert.Throws<YearEndCloseException>(() =>
            harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: false, closedBy: "x"));

        var closed = harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "x");
        Assert.Equal(FiscalYearCloseStatus.Closed, closed.Status);
    }

    [Fact]
    public void Close_WhenBooksAlreadyLockedThroughFyEnd_StillCloses_AndBlocksOtherPostings()
    {
        var lockDate = D(2025, 12, 31);
        using var harness = new Harness(booksLockedThrough: lockDate);
        harness.Post(D(2025, 6, 1), harness.CashId, harness.RevenueId, 500m);

        harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "x");
        Assert.Equal(lockDate, harness.Settings.Table.Single().BooksLockedThrough!.Value.Date);

        var entry = new GeneralLedgerHeader
        {
            CompanyId = 1,
            Date = D(2025, 6, 15),
            DocumentType = DocumentTypes.JournalEntry,
            Description = "blocked"
        };
        entry.GeneralLedgerLines.Add(new GeneralLedgerLine { AccountId = harness.CashId, DrCr = DrOrCrSide.Dr, Amount = 1m });
        entry.GeneralLedgerLines.Add(new GeneralLedgerLine { AccountId = harness.OwnerCapitalId, DrCr = DrOrCrSide.Cr, Amount = 1m });
        Assert.Throws<BooksLockedException>(() => harness.Financial.ValidateGeneralLedgerEntry(entry));
    }

    [Fact]
    public void Reopen_OnlyLatestClosedYear_ReversesClosingEntry_AndAllowsReclose()
    {
        using var harness = new Harness();
        harness.Post(D(2025, 6, 1), harness.CashId, harness.RevenueId, 800m);
        harness.Post(D(2025, 7, 1), harness.ExpenseId, harness.CashId, 300m);

        harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "close");
        var closingGlId = harness.FiscalYears.Table.Single(fy => fy.FiscalYearCode == Harness.Fy2025Code)
            .ClosingGeneralLedgerHeaderId;
        Assert.NotNull(closingGlId);

        harness.FiscalYears.Insert(new FinancialYear
        {
            CompanyId = 1,
            FiscalYearCode = "FY2024",
            FiscalYearName = "FY2024",
            StartDate = D(2024, 1, 1),
            EndDate = EndOfDay(D(2024, 12, 31)),
            IsActive = true,
            ClosedAt = DateTime.UtcNow.AddDays(-1),
            ClosedBy = "prior"
        });

        Assert.Throws<YearEndCloseException>(() =>
            harness.YearEnd.Reopen("FY2024", "too old", "x"));

        Assert.Throws<YearEndCloseException>(() =>
            harness.YearEnd.Reopen(Harness.Fy2025Code, "   ", "x"));

        var reopened = harness.YearEnd.Reopen(Harness.Fy2025Code, "Correcting accrual", "reopen");
        Assert.Equal(FiscalYearCloseStatus.Ended, reopened.Status);
        Assert.Null(harness.FiscalYears.Table.Single(fy => fy.FiscalYearCode == Harness.Fy2025Code).ClosedAt);
        Assert.Null(harness.Settings.Table.Single().BooksLockedThrough);

        var tbBeforeReclose = harness.Financial.TrialBalance(harness.Fy2025Start, harness.Fy2025End);
        Assert.True(Math.Abs(NetDr(tbBeforeReclose.Single(r => r.AccountCode == "40100"))) > 0.01m);

        var closedAgain = harness.YearEnd.Close(Harness.Fy2025Code, acknowledgeWarnings: true, closedBy: "again");
        Assert.Equal(FiscalYearCloseStatus.Closed, closedAgain.Status);
        Assert.Equal(D(2025, 12, 31), harness.Settings.Table.Single().BooksLockedThrough!.Value.Date);
    }
}

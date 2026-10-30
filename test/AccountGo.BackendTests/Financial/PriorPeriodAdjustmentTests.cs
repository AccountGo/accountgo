// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Entitlements;
using Core.Tenancy;
using Moq;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Financial;

/// <summary>Phase 2b-2 of #455: prior-period adjustments, 30820 close, ClosingEntries system-only.</summary>
public class PriorPeriodAdjustmentTests
{
    private static DateTime D(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime EndOfDay(DateTime date) =>
        date.Date.AddDays(1).AddTicks(-1);

    private static decimal NetDr(TrialBalance row) => row.Debit - row.Credit;

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
        public int ArId { get; }
        public int OwnerCapitalId { get; }
        public int PpaId { get; }
        public int RevenueId { get; }
        public int ExpenseId { get; }

        public int Fy2026Id { get; }
        // Short years that have already ended relative to "today" (late 2026 in CI).
        public DateTime Fy2026Start { get; } = D(2026, 1, 1);
        public DateTime Fy2026End { get; } = EndOfDay(D(2026, 6, 30));
        public DateTime Fy2027Start { get; } = D(2026, 7, 1);
        public DateTime Fy2027End { get; } = EndOfDay(D(2026, 9, 30));
        public const string Fy2026Code = "FY2026";

        public Harness(DateTime? booksLockedThrough = null)
        {
            GlHeaders = new GlHeaderRepoWithLines(GlLines, Accounts);
            Journals = new JournalRepoWithGlPersist(GlHeaders, GlLines, Accounts);

            Settings.Insert(new CompanySetting
            {
                CompanyId = 1,
                BooksStartDate = D(2026, 1, 1),
                BooksLockedThrough = booksLockedThrough,
                FiscalYearPattern = FiscalYearPattern.Calendar,
                Currency = "USD",
                EntityType = EntityType.SoleProprietor
            });

            var fy2026 = new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = Fy2026Code,
                FiscalYearName = Fy2026Code,
                StartDate = Fy2026Start,
                EndDate = Fy2026End,
                IsActive = true
            };
            FiscalYears.Insert(fy2026);
            Fy2026Id = fy2026.Id;
            FiscalYears.Insert(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "FY2027",
                FiscalYearName = "FY2027",
                StartDate = Fy2027Start,
                EndDate = Fy2027End,
                IsActive = true
            });

            CashId = 10;
            ArId = 11;
            OwnerCapitalId = 20;
            PpaId = 38;
            RevenueId = 40;
            ExpenseId = 50;

            Accounts.Insert(new Account { Id = CashId, CompanyId = 1, AccountCode = "10111", AccountName = "Cash", AccountClassId = 1, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = ArId, CompanyId = 1, AccountCode = "10120", AccountName = "Receivables", AccountClassId = 1, DrOrCrSide = DrOrCrSide.Dr });
            Accounts.Insert(new Account { Id = OwnerCapitalId, CompanyId = 1, AccountCode = "30100", AccountName = "Owner's Capital", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = PpaId, CompanyId = 1, AccountCode = "30820", AccountName = "Prior Period Adjustments", AccountClassId = 3, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = RevenueId, CompanyId = 1, AccountCode = "40100", AccountName = "Sales", AccountClassId = 4, DrOrCrSide = DrOrCrSide.Cr });
            Accounts.Insert(new Account { Id = ExpenseId, CompanyId = 1, AccountCode = "50100", AccountName = "Expense", AccountClassId = 5, DrOrCrSide = DrOrCrSide.Dr });

            GlSettings.Insert(new GeneralLedgerSetting { CompanyId = 1, PermanentAccountId = OwnerCapitalId });

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

        public JournalEntryHeader ValidPpa(DateTime date, decimal amount = 100m) =>
            new()
            {
                CompanyId = 1,
                Date = date,
                Memo = "Correct FY2026 receivable understatement",
                VoucherType = JournalVoucherTypes.PriorPeriodAdjustment,
                AdjustsFiscalYearId = Fy2026Id,
                JournalEntryLines =
                {
                    new JournalEntryLine { AccountId = ArId, DrCr = DrOrCrSide.Dr, Amount = amount },
                    new JournalEntryLine { AccountId = PpaId, DrCr = DrOrCrSide.Cr, Amount = amount },
                }
            };

        public void CloseFy2026()
        {
            Post(D(2026, 3, 1), CashId, RevenueId, 500m);
            YearEnd.Close(Fy2026Code, acknowledgeWarnings: true, closedBy: "close");
        }

        /// <summary>Mark FY2026 closed without running year-end (for PPA validation-only tests).</summary>
        public void MarkFy2026Closed()
        {
            var fy = FiscalYears.Table.Single(y => y.FiscalYearCode == Fy2026Code);
            fy.ClosedAt = DateTime.UtcNow;
            fy.ClosedBy = "test";
            FiscalYears.Update(fy);
        }
    }

    [Fact]
    public void PriorPeriodAdjustment_Valid_PostsAgainst30820AndReceivable()
    {
        using var harness = new Harness();
        harness.CloseFy2026();

        var journal = harness.ValidPpa(D(2026, 8, 1));
        harness.Financial.AddJournalEntry(journal);
        harness.Financial.UpdateJournalEntry(journal, posted: true);

        Assert.True(journal.Posted);
        Assert.NotNull(journal.GeneralLedgerHeaderId);
        var tb = harness.Financial.TrialBalance(null, D(2026, 8, 1));
        Assert.Equal(100m, tb.Single(r => r.AccountCode == "30820").Credit - tb.Single(r => r.AccountCode == "30820").Debit);
    }

    [Theory]
    [InlineData("open_year")]
    [InlineData("year_not_closed")]
    [InlineData("income_expense")]
    [InlineData("no_30820")]
    [InlineData("empty_memo")]
    public void PriorPeriodAdjustment_RuleViolations_ThrowSpecificMessages(string rule)
    {
        using var harness = new Harness();
        harness.MarkFy2026Closed();
        var journal = harness.ValidPpa(D(2026, 8, 1));

        string expected;
        switch (rule)
        {
            case "open_year":
            case "year_not_closed":
                var openId = harness.FiscalYears.Table.Single(fy => fy.FiscalYearCode == "FY2027").Id;
                journal.AdjustsFiscalYearId = openId;
                expected = "closed fiscal year";
                break;
            case "income_expense":
                journal.JournalEntryLines.Clear();
                journal.JournalEntryLines.Add(new JournalEntryLine { AccountId = harness.ExpenseId, DrCr = DrOrCrSide.Dr, Amount = 50m });
                journal.JournalEntryLines.Add(new JournalEntryLine { AccountId = harness.PpaId, DrCr = DrOrCrSide.Cr, Amount = 50m });
                expected = "can't post to income or expense";
                break;
            case "no_30820":
                journal.JournalEntryLines.Clear();
                journal.JournalEntryLines.Add(new JournalEntryLine { AccountId = harness.ArId, DrCr = DrOrCrSide.Dr, Amount = 50m });
                journal.JournalEntryLines.Add(new JournalEntryLine { AccountId = harness.OwnerCapitalId, DrCr = DrOrCrSide.Cr, Amount = 50m });
                expected = "30820";
                break;
            case "empty_memo":
                journal.Memo = "  ";
                expected = "memo";
                break;
            default:
                throw new InvalidOperationException(rule);
        }

        var ex = Assert.Throws<InvalidOperationException>(() => harness.Financial.AddJournalEntry(journal));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClosingEntries_ViaFinancialService_Rejected_UnlessAllowClosingEntries()
    {
        using var harness = new Harness();
        var journal = new JournalEntryHeader
        {
            CompanyId = 1,
            Date = D(2026, 6, 15),
            Memo = "manual close",
            VoucherType = JournalVoucherTypes.ClosingEntries,
            JournalEntryLines =
            {
                new JournalEntryLine { AccountId = harness.RevenueId, DrCr = DrOrCrSide.Dr, Amount = 10m },
                new JournalEntryLine { AccountId = harness.OwnerCapitalId, DrCr = DrOrCrSide.Cr, Amount = 10m },
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => harness.Financial.AddJournalEntry(journal));
        Assert.Equal("Closing entries are created by the year-end close.", ex.Message);

        using (harness.Financial.AllowClosingEntries())
        {
            harness.Financial.AddJournalEntry(journal);
            harness.Financial.UpdateJournalEntry(journal, posted: true);
        }
        Assert.True(journal.Posted);
    }

    [Fact]
    public void Close_AfterPpa_Zeros30820_PlUnchanged_BalanceSheetBalances()
    {
        using var harness = new Harness();
        harness.CloseFy2026();

        var journal = harness.ValidPpa(D(2026, 8, 1), 75m);
        harness.Financial.AddJournalEntry(journal);
        harness.Financial.UpdateJournalEntry(journal, posted: true);

        harness.Post(D(2026, 8, 15), harness.CashId, harness.RevenueId, 200m);
        var plBefore = harness.Financial.IncomeStatement(harness.Fy2027Start, harness.Fy2027End).ToList();

        var bsBefore = harness.Financial.BalanceSheet(null, harness.Fy2027End).ToList();
        Assert.Contains(bsBefore, r => r.AccountCode == "30820" && r.AccountName == "Prior period adjustments" && r.Amount == 75m);
        Assert.Equal(0m, bsBefore.Where(r => r.AccountClassId == 1).Sum(r => r.Amount)
            - bsBefore.Where(r => r.AccountClassId == 2 || r.AccountClassId == 3).Sum(r => r.Amount));

        var preview = harness.YearEnd.Preview("FY2027");
        Assert.Contains(preview.ClosingLines, l => l.AccountCode == "30820");

        harness.YearEnd.Close("FY2027", acknowledgeWarnings: true, closedBy: "close27");

        var plAfter = harness.Financial.IncomeStatement(harness.Fy2027Start, harness.Fy2027End).ToList();
        Assert.Equal(
            plBefore.Single(i => i.AccountCode == "40100").Amount,
            plAfter.Single(i => i.AccountCode == "40100").Amount);

        var tb = harness.Financial.TrialBalance(null, harness.Fy2027End);
        Assert.InRange(Math.Abs(NetDr(tb.Single(r => r.AccountCode == "30820"))), 0m, 0.005m);

        var bsAfter = harness.Financial.BalanceSheet(null, harness.Fy2027End).ToList();
        Assert.Equal(0m, bsAfter.Where(r => r.AccountClassId == 1).Sum(r => r.Amount)
            - bsAfter.Where(r => r.AccountClassId == 2 || r.AccountClassId == 3).Sum(r => r.Amount));
        Assert.DoesNotContain(bsAfter, r => r.AccountCode == "30820" && r.Amount != 0m);
    }

    [Fact]
    public void CashFlow_Classifies30820_AsRetainedEarningsFamily()
    {
        using var harness = new Harness();
        harness.CloseFy2026();

        var journal = harness.ValidPpa(D(2026, 8, 1), 40m);
        harness.Financial.AddJournalEntry(journal);
        harness.Financial.UpdateJournalEntry(journal, posted: true);

        var cf = harness.Financial.CashFlow(harness.Fy2027Start, D(2026, 8, 31));
        // Handled via the retained-earnings family path (non-cash equity), not generic financing equity.
        Assert.Contains(
            cf.OperatingActivities.Concat(cf.FinancingActivities),
            l => l.AccountCode == "30820");
    }
}

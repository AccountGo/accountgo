// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;

namespace Services.Financial
{
    public sealed class YearEndCloseService : IYearEndCloseService
    {
        private readonly IFinancialService _financial;
        private readonly IBooksLockGuard _booksLockGuard;
        private readonly ITenantContext _tenant;
        private readonly IRepository<FinancialYear> _fiscalYears;
        private readonly IRepository<CompanySetting> _settings;
        private readonly IRepository<Account> _accounts;
        private readonly IRepository<GeneralLedgerSetting> _glSettings;
        private readonly IRepository<GeneralLedgerLine> _glLines;
        private readonly IRepository<JournalEntryHeader> _journals;
        private readonly IRepository<SalesInvoiceHeader> _salesInvoices;
        private readonly IRepository<PurchaseInvoiceHeader> _purchaseInvoices;
        private readonly IRepository<Bank> _banks;
        private readonly IRepository<BankReconciliation> _reconciliations;
        private readonly IRepository<BankStatementLine> _statementLines;
        private readonly IRepository<BooksLockChange> _lockChanges;

        public YearEndCloseService(
            IFinancialService financial,
            IBooksLockGuard booksLockGuard,
            ITenantContext tenant,
            IRepository<FinancialYear> fiscalYears,
            IRepository<CompanySetting> settings,
            IRepository<Account> accounts,
            IRepository<GeneralLedgerSetting> glSettings,
            IRepository<GeneralLedgerLine> glLines,
            IRepository<JournalEntryHeader> journals,
            IRepository<SalesInvoiceHeader> salesInvoices,
            IRepository<PurchaseInvoiceHeader> purchaseInvoices,
            IRepository<Bank> banks,
            IRepository<BankReconciliation> reconciliations,
            IRepository<BankStatementLine> statementLines,
            IRepository<BooksLockChange> lockChanges)
        {
            _financial = financial;
            _booksLockGuard = booksLockGuard;
            _tenant = tenant;
            _fiscalYears = fiscalYears;
            _settings = settings;
            _accounts = accounts;
            _glSettings = glSettings;
            _glLines = glLines;
            _journals = journals;
            _salesInvoices = salesInvoices;
            _purchaseInvoices = purchaseInvoices;
            _banks = banks;
            _reconciliations = reconciliations;
            _statementLines = statementLines;
            _lockChanges = lockChanges;
        }

        public IReadOnlyList<YearEndYearSummary> ListYears()
        {
            var today = DateTime.UtcNow.Date;
            return _fiscalYears.Table
                .OrderBy(fy => fy.StartDate)
                .ToList()
                .Select(fy => ToSummary(fy, today))
                .ToList();
        }

        public YearEndPreview Preview(string fiscalYearCode)
        {
            var year = RequireYear(fiscalYearCode);
            return BuildPreview(year);
        }

        public YearEndPreview Close(string fiscalYearCode, bool acknowledgeWarnings, string closedBy)
        {
            var year = RequireYear(fiscalYearCode);
            if (year.ClosedAt != null)
                throw new YearEndCloseException("This fiscal year is already closed.");

            var preview = BuildPreview(year);
            if (preview.Blocking.Count > 0)
                throw new YearEndCloseException("Close is blocked until the checklist items are resolved.");
            if (preview.Warnings.Count > 0 && !acknowledgeWarnings)
                throw new YearEndCloseException("Acknowledge the warnings before closing the year.");

            var actor = string.IsNullOrWhiteSpace(closedBy) ? "system" : closedBy.Trim();
            int? closingGlId = null;

            using (_booksLockGuard.Bypass())
            {
                if (preview.ClosingLines.Count > 0)
                {
                    var journal = new JournalEntryHeader
                    {
                        CompanyId = _tenant.RequireCompanyId(),
                        Date = year.EndDate.Date,
                        Memo = "Closing entries " + year.FiscalYearCode,
                        Posted = false,
                        VoucherType = JournalVoucherTypes.ClosingEntries
                    };
                    foreach (var line in preview.ClosingLines)
                    {
                        journal.JournalEntryLines.Add(new JournalEntryLine
                        {
                            AccountId = line.AccountId,
                            DrCr = line.DrCr == "Dr" ? DrOrCrSide.Dr : DrOrCrSide.Cr,
                            Amount = line.Amount
                        });
                    }
                    _financial.AddJournalEntry(journal);
                    _financial.UpdateJournalEntry(journal, posted: true);
                    closingGlId = journal.GeneralLedgerHeaderId
                        ?? journal.GeneralLedgerHeader?.Id
                        ?? throw new InvalidOperationException("Closing journal did not produce a general ledger header.");
                }
            }

            year.ClosedAt = DateTime.UtcNow;
            year.ClosedBy = actor;
            year.ClosingGeneralLedgerHeaderId = closingGlId;
            _fiscalYears.Update(year);

            var setting = _settings.Table.FirstOrDefault()
                ?? throw new InvalidOperationException("Company settings were not found.");
            var previousLock = setting.BooksLockedThrough?.Date;
            var fyEnd = year.EndDate.Date;
            if (!previousLock.HasValue || previousLock.Value < fyEnd)
            {
                setting.BooksLockedThrough = fyEnd;
                _settings.Update(setting);
                _lockChanges.Insert(new BooksLockChange
                {
                    CompanyId = setting.CompanyId,
                    PreviousLockedThrough = previousLock,
                    NewLockedThrough = fyEnd,
                    Reason = "Closed " + year.FiscalYearCode,
                    ChangedBy = actor,
                    ChangedAt = DateTime.UtcNow
                });
            }

            return BuildPreview(RequireYear(fiscalYearCode));
        }

        public YearEndPreview Reopen(string fiscalYearCode, string reason, string reopenedBy)
        {
            if (string.IsNullOrWhiteSpace(reason))
                throw new YearEndCloseException("A reason is required to reopen a closed year.");

            var year = RequireYear(fiscalYearCode);
            if (year.ClosedAt == null)
                throw new YearEndCloseException("This fiscal year is not closed.");

            var latestClosed = _fiscalYears.Table
                .Where(fy => fy.ClosedAt != null)
                .OrderByDescending(fy => fy.EndDate)
                .FirstOrDefault();
            if (latestClosed == null || latestClosed.Id != year.Id)
                throw new YearEndCloseException("Only the latest closed fiscal year can be reopened.");

            var actor = string.IsNullOrWhiteSpace(reopenedBy) ? "system" : reopenedBy.Trim();

            using (_booksLockGuard.Bypass())
            {
                if (year.ClosingGeneralLedgerHeaderId.HasValue)
                {
                    var reversal = _financial.CreateReversingEntry(
                        year.ClosingGeneralLedgerHeaderId.Value,
                        "Reopen " + year.FiscalYearCode + ": " + reason.Trim(),
                        year.EndDate.Date);
                    _financial.SaveGeneralLedgerEntry(reversal);

                    // Tag the reversal as ClosingEntries so P&L / dashboard keep excluding both legs.
                    var reverseJournal = new JournalEntryHeader
                    {
                        CompanyId = _tenant.RequireCompanyId(),
                        Date = year.EndDate.Date,
                        Memo = "Reopen closing entries " + year.FiscalYearCode,
                        Posted = true,
                        VoucherType = JournalVoucherTypes.ClosingEntries,
                        GeneralLedgerHeaderId = reversal.Id,
                        GeneralLedgerHeader = reversal
                    };
                    foreach (var line in reversal.GeneralLedgerLines)
                    {
                        reverseJournal.JournalEntryLines.Add(new JournalEntryLine
                        {
                            AccountId = line.AccountId,
                            DrCr = line.DrCr,
                            Amount = line.Amount
                        });
                    }
                    _journals.Insert(reverseJournal);
                }
            }

            year.ClosedAt = null;
            year.ClosedBy = null;
            year.ClosingGeneralLedgerHeaderId = null;
            _fiscalYears.Update(year);

            var setting = _settings.Table.FirstOrDefault()
                ?? throw new InvalidOperationException("Company settings were not found.");
            var previousLock = setting.BooksLockedThrough?.Date;
            var dayBeforeStart = year.StartDate.Date.AddDays(-1);
            DateTime? nextLock = null;
            if (dayBeforeStart >= setting.BooksStartDate.Date)
                nextLock = dayBeforeStart;

            setting.BooksLockedThrough = nextLock;
            _settings.Update(setting);
            _lockChanges.Insert(new BooksLockChange
            {
                CompanyId = setting.CompanyId,
                PreviousLockedThrough = previousLock,
                NewLockedThrough = nextLock,
                Reason = reason.Trim(),
                ChangedBy = actor,
                ChangedAt = DateTime.UtcNow
            });

            return BuildPreview(RequireYear(fiscalYearCode));
        }

        private YearEndPreview BuildPreview(FinancialYear year)
        {
            var today = DateTime.UtcNow.Date;
            var status = ResolveStatus(year, today);
            var blocking = new List<YearEndCheckItem>();
            var warnings = new List<YearEndCheckItem>();

            if (status != FiscalYearCloseStatus.Closed)
            {
                AddBlockingChecks(year, today, blocking);
                AddWarningChecks(year, warnings);
            }

            var closingLines = status == FiscalYearCloseStatus.Closed
                ? Array.Empty<YearEndClosingLine>()
                : BuildClosingLines(year);

            return new YearEndPreview
            {
                FiscalYearCode = year.FiscalYearCode,
                StartDate = year.StartDate.Date,
                EndDate = year.EndDate.Date,
                Status = status,
                ClosedAt = year.ClosedAt,
                ClosedBy = year.ClosedBy,
                Blocking = blocking,
                Warnings = warnings,
                ClosingLines = closingLines
            };
        }

        private void AddBlockingChecks(FinancialYear year, DateTime today, List<YearEndCheckItem> blocking)
        {
            if (year.EndDate.Date >= today)
            {
                blocking.Add(new YearEndCheckItem
                {
                    Code = "fy_not_ended",
                    Message = "The fiscal year has not ended yet.",
                    Count = 1
                });
            }

            var setting = _settings.Table.FirstOrDefault();
            if (setting != null)
            {
                var earlierOpen = _fiscalYears.Table
                    .Where(fy => fy.IsActive
                        && fy.ClosedAt == null
                        && fy.StartDate >= setting.BooksStartDate
                        && fy.EndDate < year.StartDate)
                    .OrderBy(fy => fy.StartDate)
                    .Select(fy => fy.FiscalYearCode)
                    .ToList();
                if (earlierOpen.Count > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "earlier_year_open",
                        Message = "Close earlier fiscal years first: " + string.Join(", ", earlierOpen),
                        Count = earlierOpen.Count
                    });
                }
            }

            var obe = _accounts.Table.FirstOrDefault(a => a.AccountCode == "30700");
            if (obe != null)
            {
                var tb = TrialBalanceExcludingClosing(null, InclusiveEnd(year.EndDate));
                if (tb.TryGetValue(obe.Id, out var bal))
                {
                    var net = bal.Debit - bal.Credit;
                    if (net != 0m)
                    {
                        blocking.Add(new YearEndCheckItem
                        {
                            Code = "opening_balance_equity",
                            Message = "Opening Balance Equity must be zero before close.",
                            Count = 1,
                            LinkPath = "/financials/accounts"
                        });
                    }
                }
            }

            var draftInvoices = _salesInvoices.Table.Count(i =>
                i.Status == SalesInvoiceStatus.Draft
                && i.Date >= year.StartDate.Date
                && i.Date <= InclusiveEnd(year.EndDate));
            if (draftInvoices > 0)
            {
                blocking.Add(new YearEndCheckItem
                {
                    Code = "draft_invoices",
                    Message = "Draft sales invoices dated in the fiscal year must be issued or deleted.",
                    Count = draftInvoices,
                    LinkPath = "/sales/invoices"
                });
            }

            var draftBills = _purchaseInvoices.Table.Count(i =>
                (i.Status == null || i.Status == PurchaseInvoiceStatus.Draft)
                && i.GeneralLedgerHeaderId == null
                && i.Date >= year.StartDate.Date
                && i.Date <= InclusiveEnd(year.EndDate));
            if (draftBills > 0)
            {
                blocking.Add(new YearEndCheckItem
                {
                    Code = "draft_bills",
                    Message = "Draft bills dated in the fiscal year must be approved or deleted.",
                    Count = draftBills,
                    LinkPath = "/purchasing/bills"
                });
            }

            var unpostedJournals = _journals.Table.Count(j =>
                j.Posted != true
                && j.Date >= year.StartDate.Date
                && j.Date <= InclusiveEnd(year.EndDate));
            if (unpostedJournals > 0)
            {
                blocking.Add(new YearEndCheckItem
                {
                    Code = "unposted_journals",
                    Message = "Unposted journal entries dated in the fiscal year must be posted or deleted.",
                    Count = unpostedJournals,
                    LinkPath = "/financials/journals"
                });
            }
        }

        private void AddWarningChecks(FinancialYear year, List<YearEndCheckItem> warnings)
        {
            var fyEnd = year.EndDate.Date;
            var banks = _banks.Table.Where(b => b.IsActive).ToList();
            var unreconciled = 0;
            foreach (var bank in banks)
            {
                var latest = _reconciliations.Table
                    .Where(r => r.BankId == bank.Id && r.Status == BankReconciliationStatuses.Finished)
                    .Select(r => (DateTime?)r.StatementEndDate)
                    .Max();
                if (!latest.HasValue || latest.Value.Date < fyEnd)
                    unreconciled++;
            }
            if (unreconciled > 0)
            {
                warnings.Add(new YearEndCheckItem
                {
                    Code = "banks_unreconciled",
                    Message = "Bank accounts are not reconciled through the fiscal year end.",
                    Count = unreconciled,
                    LinkPath = "/banking"
                });
            }

            var unmatched = _statementLines.Table.Count(l =>
                l.ExcludedAt == null
                && l.MatchedGeneralLedgerLineId == null
                && l.PostedDate >= year.StartDate.Date
                && l.PostedDate <= InclusiveEnd(year.EndDate));
            if (unmatched > 0)
            {
                warnings.Add(new YearEndCheckItem
                {
                    Code = "unmatched_statement_lines",
                    Message = "Unmatched statement lines are dated in the fiscal year.",
                    Count = unmatched,
                    LinkPath = "/banking"
                });
            }

            var cutoff = fyEnd.AddDays(-365);
            var staleAr = _salesInvoices.Table.Count(i =>
                i.Status != SalesInvoiceStatus.Draft
                && i.Status != SalesInvoiceStatus.Void
                && i.Status != SalesInvoiceStatus.Closed
                && i.Date.Date <= cutoff);
            var staleAp = _purchaseInvoices.GetAllIncluding(i => i.PurchaseInvoiceLines, i => i.VendorPayments)
                .AsEnumerable()
                .Count(i => !i.IsDraft && !i.IsVoid && i.IsPayable && i.Date.Date <= cutoff && i.BalanceDue() > 0.005m);

            var stale = staleAr + staleAp;
            if (stale > 0)
            {
                warnings.Add(new YearEndCheckItem
                {
                    Code = "stale_ar_ap",
                    Message = "Open AR/AP items are older than 365 days at fiscal year end.",
                    Count = stale
                });
            }
        }

        private IReadOnlyList<YearEndClosingLine> BuildClosingLines(FinancialYear year)
        {
            var glSetting = _glSettings.Table.FirstOrDefault();
            if (glSetting?.PermanentAccountId == null)
                throw new YearEndCloseException("Permanent (retained earnings) account is not configured.");

            var permanent = _accounts.Table.FirstOrDefault(a => a.Id == glSetting.PermanentAccountId.Value)
                ?? throw new YearEndCloseException("Permanent (retained earnings) account was not found.");

            var setting = _settings.Table.FirstOrDefault();
            var entityType = setting?.EntityType ?? EntityType.Corporation;
            var profile = EntityEquityMapping.GetProfile(entityType);

            var fyTb = TrialBalanceExcludingClosing(year.StartDate.Date, InclusiveEnd(year.EndDate));
            var incomeExpense = _accounts.Table
                .Where(a => a.AccountClassId == 4 || a.AccountClassId == 5)
                .Select(a => new { a.Id, a.AccountCode, a.AccountName })
                .ToList();

            var lines = new List<YearEndClosingLine>();
            decimal permanentDr = 0m, permanentCr = 0m;

            void ZeroAccount(int accountId, string code, string name, decimal debit, decimal credit)
            {
                var netDr = debit - credit;
                if (netDr == 0m)
                    return;
                if (netDr > 0m)
                {
                    lines.Add(new YearEndClosingLine
                    {
                        AccountId = accountId,
                        AccountCode = code,
                        AccountName = name,
                        DrCr = "Cr",
                        Amount = netDr
                    });
                    permanentDr += netDr;
                }
                else
                {
                    var amount = -netDr;
                    lines.Add(new YearEndClosingLine
                    {
                        AccountId = accountId,
                        AccountCode = code,
                        AccountName = name,
                        DrCr = "Dr",
                        Amount = amount
                    });
                    permanentCr += amount;
                }
            }

            foreach (var account in incomeExpense)
            {
                if (!fyTb.TryGetValue(account.Id, out var bal))
                    continue;
                ZeroAccount(account.Id, account.AccountCode, account.AccountName, bal.Debit, bal.Credit);
            }

            if (!string.IsNullOrEmpty(profile.DistributionAccountCode))
            {
                var dist = _accounts.Table.FirstOrDefault(a => a.AccountCode == profile.DistributionAccountCode);
                if (dist != null)
                {
                    var throughEnd = TrialBalanceExcludingClosing(null, InclusiveEnd(year.EndDate));
                    if (throughEnd.TryGetValue(dist.Id, out var bal))
                        ZeroAccount(dist.Id, dist.AccountCode, dist.AccountName, bal.Debit, bal.Credit);
                }
            }

            var permanentNet = permanentCr - permanentDr;
            if (permanentNet > 0m)
            {
                lines.Add(new YearEndClosingLine
                {
                    AccountId = permanent.Id,
                    AccountCode = permanent.AccountCode,
                    AccountName = permanent.AccountName,
                    DrCr = "Cr",
                    Amount = permanentNet
                });
            }
            else if (permanentNet < 0m)
            {
                lines.Add(new YearEndClosingLine
                {
                    AccountId = permanent.Id,
                    AccountCode = permanent.AccountCode,
                    AccountName = permanent.AccountName,
                    DrCr = "Dr",
                    Amount = -permanentNet
                });
            }

            return lines;
        }

        private Dictionary<int, (decimal Debit, decimal Credit)> TrialBalanceExcludingClosing(DateTime? from, DateTime to)
        {
            var closingHeaderIds = ClosingGeneralLedgerHeaderIds();
            var raw = _glLines.GetAllIncluding(l => l.GeneralLedgerHeader)
                .Where(l => l.GeneralLedgerHeader.Date <= to
                    && (!from.HasValue || l.GeneralLedgerHeader.Date >= from.Value)
                    && !closingHeaderIds.Contains(l.GeneralLedgerHeaderId));

            return raw
                .GroupBy(l => l.AccountId)
                .ToDictionary(
                    g => g.Key,
                    g => (
                        Debit: g.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : 0m),
                        Credit: g.Sum(l => l.DrCr == DrOrCrSide.Cr ? l.Amount : 0m)));
        }

        private HashSet<int> ClosingGeneralLedgerHeaderIds() =>
            _journals.Table
                .Where(j => j.VoucherType == JournalVoucherTypes.ClosingEntries && j.GeneralLedgerHeaderId != null)
                .Select(j => j.GeneralLedgerHeaderId!.Value)
                .ToHashSet();

        private FinancialYear RequireYear(string fiscalYearCode)
        {
            if (string.IsNullOrWhiteSpace(fiscalYearCode))
                throw new YearEndCloseException("Fiscal year code is required.");

            var year = _fiscalYears.Table.FirstOrDefault(fy => fy.FiscalYearCode == fiscalYearCode);
            if (year == null)
                throw new YearEndCloseException("Fiscal year was not found.");
            return year;
        }

        private static YearEndYearSummary ToSummary(FinancialYear year, DateTime today) =>
            new()
            {
                FiscalYearCode = year.FiscalYearCode,
                StartDate = year.StartDate.Date,
                EndDate = year.EndDate.Date,
                Status = ResolveStatus(year, today),
                ClosedAt = year.ClosedAt,
                ClosedBy = year.ClosedBy
            };

        private static FiscalYearCloseStatus ResolveStatus(FinancialYear year, DateTime today)
        {
            if (year.ClosedAt != null)
                return FiscalYearCloseStatus.Closed;
            if (year.EndDate.Date < today)
                return FiscalYearCloseStatus.Ended;
            return FiscalYearCloseStatus.Open;
        }

        private static DateTime InclusiveEnd(DateTime end) =>
            end.TimeOfDay == TimeSpan.Zero ? end.Date.AddDays(1).AddTicks(-1) : end;
    }

    public sealed class YearEndCloseException : Exception
    {
        public YearEndCloseException(string message) : base(message) { }
    }
}

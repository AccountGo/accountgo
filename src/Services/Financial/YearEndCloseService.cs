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
        private readonly IRepository<Partner>? _partners;
        private readonly IRepository<PartnerShare>? _partnerShares;
        private readonly bool _allowUnapprovedIndividualMapping;

        /// <param name="allowUnapprovedIndividualMapping">
        /// Disposable-fixture seam only. Dependency injection never supplies it, so production close and reopen for an
        /// Individual workspace keep failing closed until <see cref="EntityEquityMapping.IsIndividualMappingApproved"/> is true.
        /// </param>
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
            IRepository<BooksLockChange> lockChanges,
            bool allowUnapprovedIndividualMapping = false,
            IRepository<Partner>? partners = null,
            IRepository<PartnerShare>? partnerShares = null)
        {
            _allowUnapprovedIndividualMapping = allowUnapprovedIndividualMapping;
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
            _partners = partners;
            _partnerShares = partnerShares;
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
            using (_financial.AllowClosingEntries())
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

        public YearEndPreview Reopen(string fiscalYearCode, string reason, string reopenedBy, bool acknowledgeIndependentLock = false, DateTime? expectedLockThrough = null)
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

            var setting = _settings.Table.FirstOrDefault()
                ?? throw new InvalidOperationException("Company settings were not found.");
            if (setting.EntityType == EntityType.Partnership
                && setting.PartnerAccountsFrom.HasValue
                && year.EndDate.Date < setting.PartnerAccountsFrom.Value.Date)
                throw new YearEndCloseException("A fiscal year ending before partner account setup can't be reopened.");
            var (currentLock, nextLock, independentLock) = ReopenLockImpact(year, setting);
            if (independentLock && (!acknowledgeIndependentLock || expectedLockThrough?.Date != currentLock))
                throw new YearEndCloseException("Acknowledge the independent lock change and confirm the current lock date before reopening.");

            var actor = string.IsNullOrWhiteSpace(reopenedBy) ? "system" : reopenedBy.Trim();

            using (_booksLockGuard.Bypass())
            using (_financial.AllowClosingEntries())
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

            var previousLock = currentLock;
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

        private (DateTime? Current, DateTime? Proposed, bool RequiresAcknowledgement) ReopenLockImpact(FinancialYear year, CompanySetting setting)
        {
            var current = setting.BooksLockedThrough?.Date;
            var before = year.StartDate.Date.AddDays(-1);
            DateTime? proposed = before >= setting.BooksStartDate.Date ? before : null;
            if (!current.HasValue || current.Value < year.StartDate.Date)
                return (current, current, false);

            var lastChange = _lockChanges.Table
                .Where(c => c.CompanyId == setting.CompanyId)
                .OrderByDescending(c => c.Id)
                .FirstOrDefault();
            var imposedByClose = lastChange != null
                && lastChange.Reason == "Closed " + year.FiscalYearCode
                && lastChange.NewLockedThrough?.Date == current
                && current == year.EndDate.Date;
            var priorIndependent = imposedByClose && lastChange!.PreviousLockedThrough?.Date >= year.StartDate.Date;
            return (current, proposed, !imposedByClose || priorIndependent);
        }

        private YearEndPreview BuildPreview(FinancialYear year)
        {
            var today = DateTime.UtcNow.Date;
            var status = ResolveStatus(year, today);
            var blocking = new List<YearEndCheckItem>();
            var warnings = new List<YearEndCheckItem>();
            var setting = _settings.Table.FirstOrDefault();
            var partnerMode = setting?.EntityType == EntityType.Partnership && setting.PartnerAccountsFrom.HasValue;
            Dictionary<int, (decimal Debit, decimal Credit)>? fyTb = null;
            Dictionary<int, (decimal Debit, decimal Credit)>? throughEnd = null;
            if (status != FiscalYearCloseStatus.Closed || partnerMode)
            {
                fyTb = TrialBalanceExcludingClosing(year.StartDate.Date, InclusiveEnd(year.EndDate));
                throughEnd = TrialBalanceThrough(null, InclusiveEnd(year.EndDate));
            }

            if (status != FiscalYearCloseStatus.Closed)
            {
                AddBlockingChecks(year, today, blocking, throughEnd);
                AddWarningChecks(year, warnings);
            }
            var closingLines = status == FiscalYearCloseStatus.Closed
                ? Array.Empty<YearEndClosingLine>()
                : BuildClosingLines(year, fyTb!, throughEnd!);
            var impact = setting != null && status == FiscalYearCloseStatus.Closed
                ? ReopenLockImpact(year, setting)
                : (Current: (DateTime?)null, Proposed: (DateTime?)null, RequiresAcknowledgement: false);
            var partnerAllocations = partnerMode
                ? BuildPartnerAllocations(year, fyTb!, throughEnd!, closingLines)
                : Array.Empty<PartnerAllocationPreview>();
            return new YearEndPreview
            {
                CurrentBooksLockedThrough = impact.Current,
                ProposedBooksLockedThrough = impact.Proposed,
                RequiresLockAcknowledgement = impact.RequiresAcknowledgement,
                NewlyUnlockedFrom = impact.Current.HasValue && impact.Current != impact.Proposed
                    ? impact.Proposed?.AddDays(1) ?? setting?.BooksStartDate.Date : null,
                NewlyUnlockedThrough = impact.Current.HasValue && impact.Current != impact.Proposed ? impact.Current : null,
                FiscalYearCode = year.FiscalYearCode,
                StartDate = year.StartDate.Date,
                EndDate = year.EndDate.Date,
                Status = status,
                ClosedAt = year.ClosedAt,
                ClosedBy = year.ClosedBy,
                Blocking = blocking,
                Warnings = warnings,
                ClosingLines = closingLines,
                PartnerAllocations = partnerAllocations,
                PartnerLossNotice = partnerAllocations.Sum(a => a.ProfitShare) < 0m
                    ? "Losses are shared in the same ratios."
                    : null
            };
        }

        private void AddBlockingChecks(FinancialYear year, DateTime today, List<YearEndCheckItem> blocking,
            Dictionary<int, (decimal Debit, decimal Credit)>? throughEnd = null)
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
            if (setting?.EntityType == EntityType.Partnership && setting.PartnerAccountsFrom.HasValue)
            {
                var shares = _partnerShares?.Table.Where(s => s.FinancialYearId == year.Id).ToList() ?? new List<PartnerShare>();
                var active = _partners?.Table.Where(p => p.ArchivedAt == null).ToList() ?? new List<Partner>();
                var valid = shares.Count >= 2 && shares.All(s => s.Percent > 0m && active.Any(p => p.Id == s.PartnerId))
                    && shares.Sum(s => s.Percent) == 100.0000m;
                if (!valid)
                    blocking.Add(new YearEndCheckItem { Code = "partner_shares_missing", Message = $"Profit-sharing ratios aren't set for {year.FiscalYearCode}.", Count = 1 });
                foreach (var code in new[] { "30100", "30800" })
                {
                    var account = _accounts.Table.FirstOrDefault(a => a.AccountCode == code);
                    if (account == null) continue;
                    var tb = throughEnd ?? TrialBalanceThrough(null, InclusiveEnd(year.EndDate));
                    if (tb.TryGetValue(account.Id, out var balance) && balance.Debit != balance.Credit)
                        blocking.Add(new YearEndCheckItem { Code = "partner_unassigned_balance_" + code, Message = $"Unassigned partner capital or drawings: move the balance in {code} {account.AccountName} to a partner with a journal.", Count = 1 });
                }
            }
            if (setting != null)
            {
                var knownYears = _fiscalYears.Table.Select(fy => fy.FiscalYearCode).ToHashSet();
                var missingYears = new List<string>();
                var cursor = FiscalYearCalculator.Containing(setting.BooksStartDate, setting.FiscalYearPattern, setting.FiscalYearStartMonth);
                while (cursor.Start.Date < year.StartDate.Date)
                {
                    if (!knownYears.Contains(cursor.Code)) missingYears.Add(cursor.Code);
                    cursor = FiscalYearCalculator.Containing(cursor.Start.AddYears(1), setting.FiscalYearPattern, setting.FiscalYearStartMonth);
                }
                if (missingYears.Count > 0)
                {
                    blocking.Add(new YearEndCheckItem
                    {
                        Code = "earlier_year_missing",
                        Message = "Prepare missing earlier fiscal years first: " + string.Join(", ", missingYears),
                        Count = missingYears.Count
                    });
                }

                var earlierOpen = _fiscalYears.Table
                    .Where(fy => fy.IsActive
                        && fy.ClosedAt == null
                        && fy.EndDate >= setting.BooksStartDate
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

            var companyId = _tenant.RequireCompanyId();
            var clearingId = _glSettings.Table.Where(s => s.CompanyId == companyId)
                .Select(s => s.GoodsReceiptNoteClearingAccountId).FirstOrDefault();
            var accounts = _accounts.Table.Where(a => a.CompanyId == companyId
                    && (a.IsSuspenseOrClearing || (clearingId.HasValue && a.Id == clearingId.Value)))
                .OrderBy(a => a.AccountCode).ToList();
            if (accounts.Count > 0)
            {
                var accountIds = accounts.Select(a => a.Id).ToHashSet();
                var balances = _glLines.GetAllIncluding(l => l.GeneralLedgerHeader)
                    .Where(l => l.CompanyId == companyId && accountIds.Contains(l.AccountId)
                        && l.GeneralLedgerHeader.CompanyId == companyId
                        && l.GeneralLedgerHeader.Date <= InclusiveEnd(fyEnd))
                    .AsEnumerable().GroupBy(l => l.AccountId)
                    .ToDictionary(g => g.Key, g => g.Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount));
                var affected = accounts.Where(a => balances.TryGetValue(a.Id, out var balance) && balance != 0m).ToList();
                if (affected.Count > 0)
                {
                    var details = string.Join("; ", affected.Select(a =>
                        $"{a.AccountCode} {a.AccountName}: {balances[a.Id].ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"));
                    warnings.Add(new YearEndCheckItem
                    {
                        Code = "suspense_clearing",
                        Message = $"Suspense or clearing accounts have balances (debit positive): {details}.",
                        Count = affected.Count,
                        LinkPath = "/financials/accounts"
                    });
                }
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

        private IReadOnlyList<YearEndClosingLine> BuildClosingLines(FinancialYear year,
            Dictionary<int, (decimal Debit, decimal Credit)> fyTb,
            Dictionary<int, (decimal Debit, decimal Credit)> throughEnd)
        {
            var accountRows = _accounts.Table.ToList();
            var accountsById = accountRows.ToDictionary(account => account.Id);
            var glSetting = _glSettings.Table.FirstOrDefault();
            if (glSetting?.PermanentAccountId == null)
                throw new YearEndCloseException("Permanent (retained earnings) account is not configured.");

            var permanent = accountsById.GetValueOrDefault(glSetting.PermanentAccountId.Value)
                ?? throw new YearEndCloseException("Permanent (retained earnings) account was not found.");

            var setting = _settings.Table.FirstOrDefault();
            var entityType = setting?.EntityType ?? EntityType.Corporation;
            var profile = EntityEquityMapping.GetProfile(entityType, _allowUnapprovedIndividualMapping);

            var incomeExpense = accountRows
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

            // Cumulative balances through FY end must INCLUDE earlier ClosingEntries (they already
            // zeroed prior drawings / PPAs). Excluding them would re-close those on every later close.
            bool partnerMode = entityType == EntityType.Partnership && setting?.PartnerAccountsFrom.HasValue == true;
            if (!partnerMode && !string.IsNullOrEmpty(profile.DistributionAccountCode))
            {
                var dist = accountRows.FirstOrDefault(a => a.AccountCode == profile.DistributionAccountCode);
                if (dist != null && throughEnd.TryGetValue(dist.Id, out var distBal))
                    ZeroAccount(dist.Id, dist.AccountCode, dist.AccountName, distBal.Debit, distBal.Credit);
            }

            // Prior-period adjustments (30820) are equity corrections posted in open years against a
            // closed year; close them into the permanent account like distributions (own preview line).
            var ppa = accountRows.FirstOrDefault(a => a.AccountCode == "30820");
            if (!partnerMode && ppa != null && throughEnd.TryGetValue(ppa.Id, out var ppaBal))
                ZeroAccount(ppa.Id, ppa.AccountCode, ppa.AccountName, ppaBal.Debit, ppaBal.Credit);

            if (partnerMode)
            {
                var partnerList = _partners?.Table.Where(p => p.ArchivedAt == null).ToList() ?? new List<Partner>();
                var shares = _partnerShares?.Table.Where(s => s.FinancialYearId == year.Id).ToList() ?? new List<PartnerShare>();
                var validShares = shares.Where(s => s.Percent > 0m && partnerList.Any(p => p.Id == s.PartnerId)).OrderBy(s => s.PartnerId).ToList();
                if (validShares.Count < 2 || validShares.Sum(s => s.Percent) != 100.0000m)
                    return lines;
                var ppaNet = ppa != null && throughEnd.TryGetValue(ppa.Id, out var ppaValue) ? ppaValue.Credit - ppaValue.Debit : 0m;
                if (ppa != null && ppaNet != 0m)
                {
                    lines.Add(new YearEndClosingLine { AccountId = ppa.Id, AccountCode = ppa.AccountCode, AccountName = ppa.AccountName, DrCr = ppaNet > 0m ? "Dr" : "Cr", Amount = Math.Abs(ppaNet) });
                }
                var profit = permanentCr - permanentDr + ppaNet;
                var allocations = PartnerAllocation.Allocate(profit, validShares.Select(s => (s.PartnerId, s.Percent)).ToList());
                for (var i = 0; i < validShares.Count; i++)
                {
                    var partner = partnerList.Single(p => p.Id == validShares[i].PartnerId);
                    var capital = accountsById[partner.CapitalAccountId];
                    if (allocations[i] != 0m)
                        lines.Add(new YearEndClosingLine { AccountId = capital.Id, AccountCode = capital.AccountCode, AccountName = capital.AccountName, DrCr = allocations[i] > 0m ? "Cr" : "Dr", Amount = Math.Abs(allocations[i]) });
                }
                foreach (var partner in partnerList)
                {
                    var capital = accountsById[partner.CapitalAccountId];
                    var drawings = accountsById[partner.DrawingsAccountId];
                    if (throughEnd.TryGetValue(drawings.Id, out var drawBal))
                    {
                        var netDr = drawBal.Debit - drawBal.Credit;
                        if (netDr != 0m)
                        {
                            lines.Add(new YearEndClosingLine { AccountId = drawings.Id, AccountCode = drawings.AccountCode, AccountName = drawings.AccountName, DrCr = netDr > 0m ? "Cr" : "Dr", Amount = Math.Abs(netDr) });
                            lines.Add(new YearEndClosingLine { AccountId = capital.Id, AccountCode = capital.AccountCode, AccountName = capital.AccountName, DrCr = netDr > 0m ? "Dr" : "Cr", Amount = Math.Abs(netDr) });
                        }
                    }
                }
                return lines;
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

        private IReadOnlyList<PartnerAllocationPreview> BuildPartnerAllocations(FinancialYear year,
            Dictionary<int, (decimal Debit, decimal Credit)> fyTb,
            Dictionary<int, (decimal Debit, decimal Credit)> throughEnd,
            IReadOnlyList<YearEndClosingLine> closingLines)
        {
            var setting = _settings.Table.FirstOrDefault();
            if (setting?.EntityType != EntityType.Partnership || !setting.PartnerAccountsFrom.HasValue) return Array.Empty<PartnerAllocationPreview>();
            var partners = _partners?.Table.Where(p => p.ArchivedAt == null).ToList() ?? new List<Partner>();
            var shares = _partnerShares?.Table.Where(s => s.FinancialYearId == year.Id).OrderBy(s => s.PartnerId).ToList() ?? new List<PartnerShare>();
            if (shares.Count < 2 || shares.Sum(s => s.Percent) != 100m || shares.Any(s => s.Percent <= 0m || partners.All(p => p.Id != s.PartnerId))) return Array.Empty<PartnerAllocationPreview>();
            var accounts = _accounts.Table.ToDictionary(account => account.Id);
            var netIncome = accounts.Values.Where(account => account.AccountClassId == 4 || account.AccountClassId == 5)
                .Sum(account => fyTb.TryGetValue(account.Id, out var amount) ? amount.Credit - amount.Debit : 0m);
            var ppa = accounts.Values.FirstOrDefault(account => account.AccountCode == "30820");
            var ppaNet = ppa != null && throughEnd.TryGetValue(ppa.Id, out var ppaBalance) ? ppaBalance.Credit - ppaBalance.Debit : 0m;
            var profitAllocations = PartnerAllocation.Allocate(netIncome + ppaNet, shares.Select(share => (share.PartnerId, share.Percent)).ToList());
            return shares.Select(s =>
            {
                var p = partners.Single(x => x.Id == s.PartnerId);
                var capital = accounts[p.CapitalAccountId];
                var drawings = accounts[p.DrawingsAccountId];
                var profit = profitAllocations[shares.IndexOf(s)];
                var closed = closingLines.Where(line => line.AccountId == drawings.Id).Sum(line => line.DrCr == "Cr" ? line.Amount : -line.Amount);
                var currentCapital = throughEnd.TryGetValue(capital.Id, out var bal) ? bal.Credit - bal.Debit : 0m;
                return new PartnerAllocationPreview { PartnerId = p.Id, Name = p.Name, Percent = s.Percent, ProfitShare = profit, DrawingsClosed = closed, CapitalAfterClose = year.ClosedAt.HasValue ? currentCapital : currentCapital + profit - closed };
            }).ToList();
        }

        private Dictionary<int, (decimal Debit, decimal Credit)> TrialBalanceExcludingClosing(DateTime? from, DateTime to) =>
            TrialBalanceCore(from, to, excludeClosingEntries: true);

        /// <summary>Full trial balance through <paramref name="to"/>, including ClosingEntries journals.</summary>
        private Dictionary<int, (decimal Debit, decimal Credit)> TrialBalanceThrough(DateTime? from, DateTime to) =>
            TrialBalanceCore(from, to, excludeClosingEntries: false);

        private Dictionary<int, (decimal Debit, decimal Credit)> TrialBalanceCore(DateTime? from, DateTime to, bool excludeClosingEntries)
        {
            var closingHeaderIds = excludeClosingEntries ? ClosingGeneralLedgerHeaderIds() : null;
            var raw = _glLines.GetAllIncluding(l => l.GeneralLedgerHeader)
                .Where(l => l.GeneralLedgerHeader.Date <= to
                    && (!from.HasValue || l.GeneralLedgerHeader.Date >= from.Value)
                    && (closingHeaderIds == null || !closingHeaderIds.Contains(l.GeneralLedgerHeaderId)));

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
                Id = year.Id,
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

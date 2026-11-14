// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Dto.Financial;

namespace Services.Financial;

public partial class FinancialService
{
    /// <summary>Builds the statement of changes in equity using cumulative and period trial balances.</summary>
    /// <param name="from">Inclusive start, defaulting to the fiscal year containing the end date.</param>
    /// <param name="to">Inclusive end, defaulting to today.</param>
    /// <param name="basis">Accrual or cash recognition, shared with the balance sheet.</param>
    /// <returns>Equity components, movements and an independent balance-sheet reconciliation.</returns>
    public EquityStatementDto EquityStatement(DateTime? from = null, DateTime? to = null, AccountingBasis basis = AccountingBasis.Accrual)
    {
        if (_auth == null || !_auth.Authorize(AccessOperationCatalog.FinancialsReportsView).Allowed)
            return new EquityStatementDto();
        var end = to ?? DateTime.UtcNow;
        var start = from ?? ResolveFiscalYearStartContaining(end.Date);
        if (start > end)
            throw new ArgumentException("Provide a valid date range: from must not be after to.");
        if (!Enum.IsDefined(basis))
            throw new ArgumentException("Provide a valid accounting basis: Accrual or Cash.");
        var type = _companySettingRepo.Table.Select(s => s.EntityType).FirstOrDefault();
        var setting = _companySettingRepo.Table.FirstOrDefault();
        if (type == EntityType.Individual)
            throw new InvalidOperationException("The statement of changes in equity isn't available for personal books.");
        if (PartnerAccountingMode.IsEnabled(setting) && end.Date >= setting!.PartnerAccountsFrom!.Value.Date)
            return PartnerEquityStatement(start, end, basis, setting);

        var multiple = type is EntityType.Corporation or EntityType.Cooperative;
        var earningsKey = multiple ? "earnings" : type == EntityType.NonProfit ? "netAssets" : "capital";
        var columns = multiple
            ? new List<EquityStatementColumnDto>
            {
                new("contributed", type == EntityType.Cooperative ? "Member capital" : "Share capital"),
                new("earnings", type == EntityType.Cooperative ? "Retained surplus" : "Retained earnings"),
                new("other", "Other equity")
            }
            : new List<EquityStatementColumnDto> { new(earningsKey, type switch
            {
                EntityType.Partnership => "Partners' capital",
                EntityType.NonProfit => "Net assets",
                _ => "Owner's capital"
            }) };
        var distributionCode = EntityEquityMapping.GetProfile(type).DistributionAccountCode;
        var accounts = _accountRepo.Table.Where(a => a.AccountClassId >= 3 && a.AccountClassId <= 5)
            .Select(a => new { a.Id, a.AccountCode, a.AccountClassId }).ToList();
        var equity = accounts.Where(a => a.AccountClassId == 3).ToList();
        var profitIds = accounts.Where(a => a.AccountClassId is 4 or 5).Select(a => a.Id).ToHashSet();
        string Component(string code) => !multiple ? earningsKey : code switch
        {
            "30100" or "30200" => "contributed",
            "30300" or "30400" or "30500" or "30810" or "30820" => "earnings",
            _ => "other"
        };

        // Three trial balances plus the fiscal-year aggregate used by the balance-sheet split.
        // Reuse the closing aggregate with that same balance-sheet builder to avoid reading it twice.
        var opening = start == DateTime.MinValue ? new Dictionary<int, TrialBalance>()
            : TrialBalance(null, start.AddTicks(-1), basis).ToDictionary(r => r.AccountId);
        var closing = TrialBalance(null, end, basis).ToDictionary(r => r.AccountId);
        var movement = TrialBalanceExcludingClosingEntries(start, end, basis).ToDictionary(r => r.AccountId);
        static decimal Net(Dictionary<int, TrialBalance> balances, int id) =>
            balances.TryGetValue(id, out var row) ? row.Credit - row.Debit : 0m;
        Dictionary<string, decimal> Empty() => columns.ToDictionary(c => c.Key, _ => 0m);
        Dictionary<string, decimal> Position(Dictionary<int, TrialBalance> balances)
        {
            var result = Empty();
            foreach (var account in equity) result[Component(account.AccountCode)] += Net(balances, account.Id);
            result[earningsKey] += profitIds.Sum(id => Net(balances, id));
            return result;
        }
        var rows = new List<EquityStatementRowDto>();
        void Add(string key, string label, Dictionary<string, decimal> amounts, bool required = false)
        {
            if (required || amounts.Values.Any(v => v != 0m))
                rows.Add(new() { Key = key, Label = label, Amounts = amounts, Total = amounts.Values.Sum() });
        }
        var openingAmounts = Position(opening);
        var openingDate = start.Date == DateTime.MinValue ? start : start.Date.AddDays(-1);
        Add("opening", "Balance at " + openingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), openingAmounts, true);
        var adjustments = Empty();
        var contributions = Empty();
        var distributions = Empty();
        var other = Empty();
        foreach (var account in equity)
        {
            var bucket = account.AccountCode == "30820" ? adjustments
                : account.AccountCode is "30100" or "30200" ? contributions
                : account.AccountCode == distributionCode ? distributions : other;
            bucket[Component(account.AccountCode)] += Net(movement, account.Id);
        }
        Add("priorPeriodAdjustments", "Prior-period adjustments", adjustments);
        if (adjustments.Values.Any(v => v != 0m))
            Add("restatedOpening", "Restated balance", columns.ToDictionary(c => c.Key, c => openingAmounts[c.Key] + adjustments[c.Key]), true);
        var profit = Empty();
        profit[earningsKey] = profitIds.Sum(id => Net(movement, id));
        Add("profit", "Profit (loss) for the period", profit, true);
        Add("contributions", type switch
        {
            EntityType.Partnership => "Partner contributions",
            EntityType.Corporation => "Shares issued",
            EntityType.Cooperative => "Member capital contributed",
            EntityType.NonProfit => "Contributions",
            _ => "Owner contributions"
        }, contributions);
        if (distributionCode != null)
            Add("distributions", type == EntityType.Corporation ? "Dividends declared"
                : type == EntityType.Partnership ? "Partner drawings" : "Owner drawings", distributions);
        Add("other", "Other equity movements", other);
        var closingAmounts = Position(closing);
        Add("closing", "Balance at " + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), closingAmounts, true);
        if (multiple && rows.All(r => r.Amounts["other"] == 0m))
        {
            columns.RemoveAll(c => c.Key == "other");
            foreach (var row in rows) row.Amounts.Remove("other");
        }
        var balanceSheetEquity = BuildBalanceSheet(end, basis, closing).Where(r => r.AccountClassId == 3).Sum(r => r.Amount);
        return new EquityStatementDto
        {
            EntityType = type.ToString(), From = start, To = end, Basis = basis.ToString(),
            Columns = columns, Rows = rows, BalanceSheetEquity = balanceSheetEquity,
            DifferenceToBalanceSheet = rows[^1].Total - balanceSheetEquity
        };
    }

    private EquityStatementDto PartnerEquityStatement(DateTime start, DateTime end, AccountingBasis basis, CompanySetting setting)
    {
        var partners = (_partnerRepo?.Table ?? Array.Empty<Partner>().AsQueryable()).Where(p => p.CompanyId == setting.CompanyId).OrderBy(p => p.Id).ToList();
        var byAccount = partners.SelectMany(p => new[] { (p.CapitalAccountId, p.Id), (p.DrawingsAccountId, p.Id) }).ToDictionary(x => x.Item1, x => x.Id);
        var accountRows = _accountRepo.Table.Where(a => a.AccountClassId >= 3 && a.AccountClassId <= 5)
            .Select(a => new { a.Id, a.AccountCode, a.AccountClassId }).ToList();
        var accountById = accountRows.ToDictionary(a => a.Id);
        var profitIds = accountRows.Where(a => a.AccountClassId is 4 or 5).Select(a => a.Id).ToHashSet();
        var closingHeaderIds = _journalEntryRepo.Table.Where(j => j.CompanyId == setting.CompanyId
                && j.VoucherType == JournalVoucherTypes.ClosingEntries && j.GeneralLedgerHeaderId.HasValue)
            .Select(j => j.GeneralLedgerHeaderId!.Value).ToHashSet();
        // Reopen creates a reversing GL header linked to the original closing header. Include
        // those lines so the allocation row nets to zero for a reopened fiscal year.
        var closingAndReversalHeaderIds = new HashSet<int>(closingHeaderIds);
        foreach (var reversalId in _generalLedgerRepository.Table
                     .Where(h => h.CompanyId == setting.CompanyId && h.ReversedGeneralLedgerHeaderId.HasValue
                         && closingHeaderIds.Contains(h.ReversedGeneralLedgerHeaderId.Value))
                     .Select(h => h.Id))
            closingAndReversalHeaderIds.Add(reversalId);
        var setupHeaderId = setting.PartnerSetupJournalEntryId;
        var setupDate = setupHeaderId.HasValue ? _generalLedgerRepository.Table.Where(h => h.Id == setupHeaderId.Value).Select(h => (DateTime?)h.Date).FirstOrDefault() : null;
        var includeSetup = setupHeaderId.HasValue && setupDate.HasValue && setupDate.Value.Date >= start.Date && setupDate.Value.Date <= end.Date;
        var columns = partners.Select(p => new EquityStatementColumnDto("partner:" + p.Id.ToString(CultureInfo.InvariantCulture), p.ArchivedAt == null ? p.Name : p.Name + " (archived)")).ToList();
        columns.Add(new("unallocated", "Unallocated"));
        string KeyFor(int accountId) => byAccount.TryGetValue(accountId, out var partnerId)
            ? "partner:" + partnerId.ToString(CultureInfo.InvariantCulture) : "unallocated";
        Dictionary<string, decimal> Empty() => columns.ToDictionary(c => c.Key, _ => 0m);
        static decimal Net(Dictionary<int, TrialBalance> balances, int id) => balances.TryGetValue(id, out var row) ? row.Credit - row.Debit : 0m;
        var opening = start == DateTime.MinValue ? new Dictionary<int, TrialBalance>() : TrialBalance(null, start.AddTicks(-1), basis).ToDictionary(r => r.AccountId);
        var closing = TrialBalance(null, end, basis).ToDictionary(r => r.AccountId);
        var movement = TrialBalanceExcludingClosingEntries(start, end, basis).ToDictionary(r => r.AccountId);
        var setupLines = includeSetup ? _generalLedgerLineRepository.Table.Where(l => l.CompanyId == setting.CompanyId && l.GeneralLedgerHeaderId == setupHeaderId)
            .Select(l => new { l.AccountId, l.DrCr, l.Amount }).ToList() : new();
        var setupAmounts = setupLines.GroupBy(l => l.AccountId).ToDictionary(g => g.Key, g => g.Sum(l => l.DrCr == DrOrCrSide.Cr ? l.Amount : -l.Amount));
        Dictionary<string, decimal> SetupAmounts()
        {
            var result = Empty();
            foreach (var item in setupAmounts) if (accountById.ContainsKey(item.Key)) result[KeyFor(item.Key)] += item.Value;
            return result;
        }
        Dictionary<string, decimal> Position(Dictionary<int, TrialBalance> balances)
        {
            var result = Empty();
            foreach (var account in accountRows.Where(a => a.AccountClassId == 3)) result[KeyFor(account.Id)] += Net(balances, account.Id);
            foreach (var id in profitIds) result["unallocated"] += Net(balances, id);
            return result;
        }
        var rows = new List<EquityStatementRowDto>();
        void Add(string key, string label, Dictionary<string, decimal> amounts, bool required = false)
        { if (required || amounts.Values.Any(v => v != 0m)) rows.Add(new() { Key = key, Label = label, Amounts = amounts, Total = amounts.Values.Sum() }); }
        var openingAmounts = Position(opening);
        Add("opening", "Balance at " + (start.Date == DateTime.MinValue ? start : start.Date.AddDays(-1)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), openingAmounts, true);
        var adjustments = Empty(); var contributions = Empty(); var distributions = Empty(); var other = Empty();
        foreach (var account in accountRows.Where(a => a.AccountClassId == 3))
        {
            var amount = Net(movement, account.Id) - setupAmounts.GetValueOrDefault(account.Id);
            var code = account.AccountCode;
            var target = code == "30820" ? adjustments
                : code == "30100" || byAccount.ContainsKey(account.Id) && partners.Any(p => p.CapitalAccountId == account.Id) ? contributions
                : code == "30800" || byAccount.ContainsKey(account.Id) && partners.Any(p => p.DrawingsAccountId == account.Id) ? distributions : other;
            target[KeyFor(account.Id)] += amount;
        }
        Add("priorPeriodAdjustments", "Prior-period adjustments", adjustments);
        if (adjustments.Values.Any(v => v != 0m)) Add("restatedOpening", "Restated balance", columns.ToDictionary(c => c.Key, c => openingAmounts[c.Key] + adjustments[c.Key]), true);
        var profit = Empty(); profit["unallocated"] = profitIds.Sum(id => Net(movement, id));
        Add("profit", "Profit (loss) for the period", profit, true);
        if (includeSetup) Add("partnerSetup", "Balances assigned to partners", SetupAmounts());
        Add("profitAllocated", "Profit allocated to partners", PartnerClosingMovement());
        Add("contributions", "Partner contributions", contributions);
        Add("distributions", "Partner drawings", distributions);
        Add("other", "Other equity movements", other);
        Add("closing", "Balance at " + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Position(closing), true);
        // Keep archived partner columns only while they explain a non-zero balance or movement.
        var archivedIds = partners.Where(p => p.ArchivedAt != null).Select(p => p.Id).ToHashSet();
        columns = columns.Where(c => c.Key == "unallocated" || !archivedIds.Contains(ParsePartnerColumn(c.Key))
            || rows.Any(r => r.Amounts.GetValueOrDefault(c.Key) != 0m)).ToList();
        foreach (var row in rows) row.Amounts = columns.ToDictionary(c => c.Key, c => row.Amounts.GetValueOrDefault(c.Key));
        if (columns.All(c => c.Key != "unallocated" || rows.All(r => r.Amounts.GetValueOrDefault(c.Key) == 0m)))
        {
            columns.RemoveAll(c => c.Key == "unallocated");
            foreach (var row in rows) row.Amounts.Remove("unallocated");
        }
        var balanceSheetEquity = BuildBalanceSheet(end, basis, closing).Where(r => r.AccountClassId == 3).Sum(r => r.Amount);
        var hasUnallocatedProfit = profitIds.Sum(id => Net(closing, id)) != 0m;
        return new EquityStatementDto { EntityType = EntityType.Partnership.ToString(), From = start, To = end, Basis = basis.ToString(), PartnerMode = true, HasUnallocatedProfit = hasUnallocatedProfit,
            Columns = columns, Rows = rows, BalanceSheetEquity = balanceSheetEquity, DifferenceToBalanceSheet = rows[^1].Total - balanceSheetEquity };

        Dictionary<string, decimal> PartnerClosingMovement()
        {
            var result = Empty();
            if (closingAndReversalHeaderIds.Count == 0) return result;
            var lines = _generalLedgerLineRepository.Table.Where(l => l.CompanyId == setting.CompanyId && closingAndReversalHeaderIds.Contains(l.GeneralLedgerHeaderId)
                    && l.GeneralLedgerHeader.Date >= start && l.GeneralLedgerHeader.Date <= end)
                .Select(l => new { l.AccountId, l.DrCr, l.Amount }).ToList();
            foreach (var line in lines) if (accountById.ContainsKey(line.AccountId))
                result[KeyFor(line.AccountId)] += line.DrCr == DrOrCrSide.Cr ? line.Amount : -line.Amount;
            return result;
        }
    }

    private static int ParsePartnerColumn(string key) => key.StartsWith("partner:", StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(8), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : -1;
}

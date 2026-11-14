// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Authorization;
using Core.Domain;
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
        if (type == EntityType.Individual)
            throw new InvalidOperationException("The statement of changes in equity isn't available for personal books.");

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
}

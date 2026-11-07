// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Core.Domain;

namespace Services.Financial
{
    public enum FinancialReportKind
    {
        TrialBalance,
        IncomeStatement,
        BalanceSheet,
        CashFlow,
    }

    public enum FinancialReportLineKind
    {
        Account,
        Section,
        Total,
    }

    /// <summary>Flat chart row used to classify cost of goods sold the same way the reports page does.</summary>
    public sealed class FinancialReportAccountNode
    {
        public int Id { get; init; }
        public int? ParentAccountId { get; init; }
        public string? AccountName { get; init; }
    }

    public sealed class FinancialReportCashMovementLine
    {
        public DateTime Date { get; init; }
        public int GeneralLedgerHeaderId { get; init; }
        public string? Description { get; init; }
        public string AccountName { get; init; } = "";
        public string CounterpartAccountName { get; init; } = "";
        public decimal MoneyIn { get; init; }
        public decimal MoneyOut { get; init; }
    }

    public sealed class FinancialReportCashMovement
    {
        public decimal OpeningCash { get; init; }
        public decimal TotalIn { get; init; }
        public decimal TotalOut { get; init; }
        public decimal ClosingCash { get; init; }
        public IReadOnlyList<FinancialReportCashMovementLine> Lines { get; init; } = Array.Empty<FinancialReportCashMovementLine>();
    }

    public sealed class FinancialReportLine
    {
        public string Code { get; init; } = "";
        public string Label { get; init; } = "";
        public FinancialReportLineKind Kind { get; init; }
        public decimal? Debit { get; init; }
        public decimal? Credit { get; init; }
        public decimal? Amount { get; init; }
        public decimal? ComparativeAmount { get; init; }
        public decimal? Change { get; init; }
        public decimal? ChangePercent { get; init; }
    }

    /// <summary>Print model for one financial statement. Amounts are currency units, not cents.</summary>
    public sealed class FinancialReportPrint
    {
        public string Heading { get; init; } = "";
        public string? TradingAs { get; init; }
        public string Title { get; init; } = "";
        public string? Subtitle { get; init; }
        public string PeriodLabel { get; init; } = "";
        public string? ComparativePeriodLabel { get; init; }
        public bool ShowComparison { get; init; }
        public string BasisLabel { get; init; } = "";
        public string CurrencyCode { get; init; } = "USD";
        public string? Footnote { get; init; }
        public string? Notice { get; init; }
        public string? BalanceStatus { get; init; }
        public string? EmptyNote { get; init; }
        public IReadOnlyList<FinancialReportLine> Lines { get; init; } = Array.Empty<FinancialReportLine>();
        public FinancialReportCashMovement? CashMovement { get; init; }
        public string FileStem { get; init; } = "report";
        public string GeneratedLabel { get; init; } = "";
    }

    public sealed class FinancialReportRequest
    {
        public FinancialReportKind Kind { get; init; }
        public bool Personal { get; init; }
        public bool HideZero { get; init; } = true;
        public string CompanyName { get; init; } = "";
        public string? LegalName { get; init; }
        public string CurrencyCode { get; init; } = "USD";
        public AccountingBasis Basis { get; init; } = AccountingBasis.Accrual;
        public DateTime? From { get; init; }
        public DateTime? To { get; init; }
        public DateTime? CompareFrom { get; init; }
        public DateTime? CompareTo { get; init; }
        public string? Slug { get; init; }
        public DateTime GeneratedAtUtc { get; init; }
        public string? TimeZoneId { get; init; }
        public IReadOnlyList<TrialBalance>? TrialBalance { get; init; }
        public IReadOnlyList<IncomeStatement>? Income { get; init; }
        public IReadOnlyList<BalanceSheet>? BalanceSheet { get; init; }
        public CashFlowStatement? CashFlow { get; init; }
        public IReadOnlyList<FinancialReportAccountNode>? Accounts { get; init; }
        public IReadOnlyList<IncomeStatement>? ComparativeIncome { get; init; }
        public IReadOnlyList<BalanceSheet>? ComparativeBalanceSheet { get; init; }
        public FinancialReportCashMovement? CashMovement { get; init; }
    }

    /// <summary>
    /// Builds the printed statement from ledger rows. Netting, cost-of-goods classification, totals,
    /// and comparative row order match the reports page: an account that exists only in the prior
    /// period is appended after the current statement, the same way <c>applyComparativeAmounts</c> does.
    /// </summary>
    public static class FinancialReportComposer
    {
        private static readonly Regex CostName = new(
            @"\b(cost of (goods sold|sales)|direct costs?|purchases?)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static FinancialReportPrint Compose(FinancialReportRequest request)
        {
            var currency = string.IsNullOrWhiteSpace(request.CurrencyCode) ? "USD" : request.CurrencyCode.Trim().ToUpperInvariant();
            var personal = request.Personal;
            var showComparison = request.CompareTo.HasValue
                && (request.Kind == FinancialReportKind.IncomeStatement || request.Kind == FinancialReportKind.BalanceSheet);
            var lines = BuildLines(request, showComparison);
            var movement = personal && request.Kind == FinancialReportKind.CashFlow ? request.CashMovement : null;
            if (movement != null)
                lines = Array.Empty<FinancialReportLine>();

            var personalRegister = personal && request.Kind == FinancialReportKind.CashFlow;
            var hasAccounts = lines.Any(line => line.Kind == FinancialReportLineKind.Account)
                || (movement?.Lines.Count ?? 0) > 0;
            string? balanceStatus = null;
            if (movement == null && request.Kind != FinancialReportKind.IncomeStatement && lines.Count > 0)
            {
                var difference = request.Kind == FinancialReportKind.TrialBalance
                    ? (lines[^1].Debit ?? 0m) - (lines[^1].Credit ?? 0m)
                    : lines[^1].Amount ?? 0m;
                balanceStatus = difference == 0m
                    ? "In balance"
                    : "Out of balance by " + FormatStatusAmount(difference, currency);
            }

            var (title, subtitle) = Titles(request.Kind, personal);
            return new FinancialReportPrint
            {
                Heading = HeadingName(request.LegalName, request.CompanyName),
                TradingAs = TradingAs(request.LegalName, request.CompanyName),
                Title = title,
                Subtitle = subtitle,
                PeriodLabel = PeriodLabel(request.Kind, request.From, request.To, currency),
                ComparativePeriodLabel = showComparison
                    ? "Prior period: " + PeriodLabel(request.Kind, request.CompareFrom, request.CompareTo, currency)
                    : null,
                ShowComparison = showComparison,
                BasisLabel = movement != null ? "" : (request.Basis == AccountingBasis.Cash ? "Cash basis" : "Accrual basis"),
                CurrencyCode = currency,
                Footnote = Footnote(request.Kind, personal, movement != null),
                Notice = movement == null ? request.CashFlow?.Message : null,
                BalanceStatus = balanceStatus,
                EmptyNote = hasAccounts || personalRegister ? null : "No ledger activity for the selected dates and filters.",
                Lines = lines,
                CashMovement = movement,
                FileStem = FileStem(request, personal),
                GeneratedLabel = FormatGenerated(request.GeneratedAtUtc, request.TimeZoneId),
            };
        }

        private static IReadOnlyList<FinancialReportLine> BuildLines(FinancialReportRequest request, bool showComparison)
        {
            if (request.Personal && request.Kind == FinancialReportKind.CashFlow)
                return Array.Empty<FinancialReportLine>();

            var current = BuildKind(request.Kind, request, comparative: false);
            if (!showComparison)
                return current.Select(ToSingle).ToList();

            var prior = BuildKind(request.Kind, request, comparative: true);
            return MergeComparison(current, prior);
        }

        private static List<WorkLine> BuildKind(FinancialReportKind kind, FinancialReportRequest request, bool comparative)
        {
            var hideZero = request.HideZero;
            return kind switch
            {
                FinancialReportKind.TrialBalance => BuildTrialBalance(request.TrialBalance, hideZero),
                FinancialReportKind.IncomeStatement => BuildIncome(
                    comparative ? request.ComparativeIncome : request.Income,
                    request.Accounts,
                    hideZero,
                    request.Personal),
                FinancialReportKind.BalanceSheet => BuildBalanceSheet(
                    comparative ? request.ComparativeBalanceSheet : request.BalanceSheet,
                    hideZero,
                    request.Personal),
                FinancialReportKind.CashFlow => BuildCashFlow(request.CashFlow, hideZero),
                _ => new List<WorkLine>(),
            };
        }

        private static List<WorkLine> BuildTrialBalance(IReadOnlyList<TrialBalance>? rows, bool hideZero)
        {
            var lines = (rows ?? Array.Empty<TrialBalance>())
                .Select(row =>
                {
                    var net = Cents(row.Debit) - Cents(row.Credit);
                    return new WorkLine
                    {
                        Code = row.AccountCode ?? "",
                        Label = row.AccountName ?? "",
                        Kind = FinancialReportLineKind.Account,
                        AccountId = row.AccountId,
                        Debit = net > 0m ? net : 0m,
                        Credit = net < 0m ? -net : 0m,
                    };
                })
                .Where(row => !hideZero || row.Debit != 0m || row.Credit != 0m)
                .OrderBy(row => row.Code, NaturalCodeComparer.Instance)
                .ToList();
            var debit = lines.Sum(row => row.Debit ?? 0m);
            var credit = lines.Sum(row => row.Credit ?? 0m);
            lines.Add(new WorkLine
            {
                Label = "Total Debits / Total Credits",
                Kind = FinancialReportLineKind.Total,
                Debit = debit,
                Credit = credit,
            });
            lines.Add(new WorkLine
            {
                Label = "Difference",
                Kind = FinancialReportLineKind.Total,
                Debit = debit > credit ? debit - credit : 0m,
                Credit = credit > debit ? credit - debit : 0m,
            });
            return lines;
        }

        private static List<WorkLine> BuildIncome(
            IReadOnlyList<IncomeStatement>? rows,
            IReadOnlyList<FinancialReportAccountNode>? accounts,
            bool hideZero,
            bool personal)
        {
            var income = rows ?? Array.Empty<IncomeStatement>();
            var lines = new List<WorkLine>();
            if (personal)
            {
                var earned = Section(lines, "Earned income", income.Where(row => !row.IsExpense && row.AccountCode != "40900"), hideZero);
                var freelance = income.Where(row => !row.IsExpense && row.AccountCode == "40900").ToList();
                var freelanceAmount = freelance.Sum(row => Cents(row.Amount));
                lines.Add(new WorkLine
                {
                    Code = "40900",
                    Label = "Money from my freelance work",
                    Kind = FinancialReportLineKind.Account,
                    Amount = freelanceAmount,
                });
                var spending = Section(lines, "Spending", income.Where(row => row.IsExpense), hideZero);
                lines.Add(Total("Net", earned + freelanceAmount - spending));
                return lines;
            }

            var costIds = CostAccountIds(accounts);
            var revenues = Section(lines, "Revenue", income.Where(row => !row.IsExpense), hideZero);
            var costs = Section(lines, "Cost of Goods Sold", income.Where(row => row.IsExpense && costIds.Contains(row.AccountId)), hideZero);
            lines.Add(Total("Gross Profit", revenues - costs));
            var expenses = Section(lines, "Operating Expenses", income.Where(row => row.IsExpense && !costIds.Contains(row.AccountId)), hideZero);
            lines.Add(Total("Net Profit", revenues - costs - expenses));
            return lines;
        }

        private static List<WorkLine> BuildBalanceSheet(IReadOnlyList<BalanceSheet>? rows, bool hideZero, bool personal)
        {
            var sheet = rows ?? Array.Empty<BalanceSheet>();
            var lines = new List<WorkLine>();
            var assets = Section(lines, "Assets", sheet.Where(row => row.AccountClassId == 1), hideZero);
            var liabilities = Section(lines, personal ? "Debts" : "Liabilities", sheet.Where(row => row.AccountClassId == 2), hideZero);
            var equity = Section(lines, personal ? "Net worth" : "Equity", sheet.Where(row => row.AccountClassId == 3), hideZero);
            lines.Add(Total(personal ? "Total debts and net worth" : "Total Liabilities + Equity", liabilities + equity));
            lines.Add(Total("Difference", assets - liabilities - equity));
            return lines;
        }

        private static List<WorkLine> BuildCashFlow(CashFlowStatement? statement, bool hideZero)
        {
            var flow = statement ?? new CashFlowStatement();
            var lines = new List<WorkLine>();
            if (!string.IsNullOrWhiteSpace(flow.Message))
            {
                lines.Add(new WorkLine { Label = flow.Message, Kind = FinancialReportLineKind.Section });
            }
            else
            {
                lines.Add(new WorkLine { Label = "Cash flows from operating activities", Kind = FinancialReportLineKind.Section });
                lines.Add(new WorkLine { Label = "Net Profit", Kind = FinancialReportLineKind.Account, Amount = Cents(flow.NetIncome) });
                AddActivity(lines, flow.OperatingActivities, hideZero);
                lines.Add(Total("Net cash provided by operating activities", Cents(flow.OperatingCashFlow)));

                lines.Add(new WorkLine { Label = "Cash flows from investing activities", Kind = FinancialReportLineKind.Section });
                AddActivity(lines, flow.InvestingActivities, hideZero);
                lines.Add(Total("Net cash provided by investing activities", Cents(flow.InvestingCashFlow)));

                lines.Add(new WorkLine { Label = "Cash flows from financing activities", Kind = FinancialReportLineKind.Section });
                AddActivity(lines, flow.FinancingActivities, hideZero);
                lines.Add(Total("Net cash provided by financing activities", Cents(flow.FinancingCashFlow)));
            }

            lines.Add(new WorkLine { Label = "Cash and cash equivalents", Kind = FinancialReportLineKind.Section });
            lines.Add(Total("Net increase / (decrease) in cash", Cents(flow.NetChangeInCash)));
            lines.Add(new WorkLine { Label = "Cash at beginning of period", Kind = FinancialReportLineKind.Account, Amount = Cents(flow.StartingCash) });
            lines.Add(Total("Cash at end of period", Cents(flow.EndingCash)));
            var difference = string.IsNullOrWhiteSpace(flow.Message)
                ? Cents(flow.OperatingCashFlow) + Cents(flow.InvestingCashFlow) + Cents(flow.FinancingCashFlow) - Cents(flow.NetChangeInCash)
                : Cents(flow.StartingCash) + Cents(flow.NetChangeInCash) - Cents(flow.EndingCash);
            lines.Add(Total("Difference", difference));
            return lines;
        }

        private static void AddActivity(List<WorkLine> lines, IEnumerable<CashFlowLine>? rows, bool hideZero)
        {
            foreach (var row in (rows ?? Array.Empty<CashFlowLine>()).OrderBy(row => row.AccountCode ?? "", NaturalCodeComparer.Instance))
            {
                var amount = Cents(row.Amount);
                if (hideZero && amount == 0m)
                    continue;
                lines.Add(new WorkLine
                {
                    Code = row.AccountCode ?? "",
                    Label = row.AccountName ?? "",
                    Kind = FinancialReportLineKind.Account,
                    AccountId = row.AccountId == 0 ? null : row.AccountId,
                    Amount = amount,
                });
            }
        }

        private static decimal Section(List<WorkLine> lines, string label, IEnumerable<IncomeStatement> rows, bool hideZero)
        {
            return Section(lines, label, rows.Select(row => (row.AccountId, row.AccountCode, row.AccountName, row.Amount)), hideZero);
        }

        private static decimal Section(List<WorkLine> lines, string label, IEnumerable<BalanceSheet> rows, bool hideZero)
        {
            return Section(lines, label, rows.Select(row => (row.AccountId, row.AccountCode, row.AccountName, row.Amount)), hideZero);
        }

        private static decimal Section(
            List<WorkLine> lines,
            string label,
            IEnumerable<(int AccountId, string? AccountCode, string? AccountName, decimal Amount)> rows,
            bool hideZero)
        {
            var list = rows.ToList();
            lines.Add(new WorkLine { Label = label, Kind = FinancialReportLineKind.Section });
            foreach (var row in list.Where(row => !hideZero || Cents(row.Amount) != 0m).OrderBy(row => row.AccountCode ?? "", NaturalCodeComparer.Instance))
            {
                lines.Add(new WorkLine
                {
                    Code = row.AccountCode ?? "",
                    Label = row.AccountName ?? "",
                    Kind = FinancialReportLineKind.Account,
                    AccountId = row.AccountId == 0 ? null : row.AccountId,
                    Amount = Cents(row.Amount),
                });
            }
            var total = list.Sum(row => Cents(row.Amount));
            lines.Add(Total("Total " + label, total));
            return total;
        }

        private static WorkLine Total(string label, decimal amount) => new()
        {
            Label = label,
            Kind = FinancialReportLineKind.Total,
            Amount = amount,
        };

        private static List<FinancialReportLine> MergeComparison(List<WorkLine> current, List<WorkLine> comparative)
        {
            var priorByKey = new Dictionary<string, WorkLine>();
            foreach (var row in comparative)
                priorByKey[Key(row)] = row;

            var seen = new HashSet<string>();
            var merged = new List<FinancialReportLine>();
            foreach (var row in current)
            {
                var key = Key(row);
                seen.Add(key);
                if (row.Kind == FinancialReportLineKind.Section)
                {
                    merged.Add(ToSingle(row));
                    continue;
                }
                priorByKey.TryGetValue(key, out var other);
                merged.Add(ToCompared(row, other));
            }

            foreach (var row in comparative)
            {
                if (!seen.Add(Key(row)) || row.Kind == FinancialReportLineKind.Section)
                    continue;
                merged.Add(ToCompared(ZeroAmount(row), row));
            }
            return merged;
        }

        private static FinancialReportLine ToSingle(WorkLine row) => new()
        {
            Code = row.Code,
            Label = row.Label,
            Kind = row.Kind,
            Debit = row.Debit,
            Credit = row.Credit,
            Amount = row.Kind == FinancialReportLineKind.Section ? null : row.Amount,
        };

        private static FinancialReportLine ToCompared(WorkLine row, WorkLine? other)
        {
            var amount = row.Amount ?? 0m;
            var comparative = other?.Amount ?? 0m;
            var change = amount - comparative;
            return new FinancialReportLine
            {
                Code = row.Code,
                Label = row.Label,
                Kind = row.Kind,
                Amount = amount,
                ComparativeAmount = comparative,
                Change = change,
                ChangePercent = comparative == 0m ? null : change / Math.Abs(comparative) * 100m,
            };
        }

        private static WorkLine ZeroAmount(WorkLine row) => new()
        {
            Code = row.Code,
            Label = row.Label,
            Kind = row.Kind,
            AccountId = row.AccountId,
            Amount = 0m,
        };

        private static string Key(WorkLine row) =>
            row.Kind == FinancialReportLineKind.Account && row.AccountId is int id
                ? "a:" + id.ToString(CultureInfo.InvariantCulture)
                : row.Kind + ":" + row.Code + ":" + row.Label;

        private static HashSet<int> CostAccountIds(IReadOnlyList<FinancialReportAccountNode>? accounts)
        {
            var list = accounts ?? Array.Empty<FinancialReportAccountNode>();
            var known = list.Select(account => account.Id).ToHashSet();
            var children = new Dictionary<int, List<FinancialReportAccountNode>>();
            var roots = new List<FinancialReportAccountNode>();
            foreach (var account in list)
            {
                if (account.ParentAccountId is int parent && known.Contains(parent))
                {
                    if (!children.TryGetValue(parent, out var kids))
                        children[parent] = kids = new List<FinancialReportAccountNode>();
                    kids.Add(account);
                }
                else
                {
                    roots.Add(account);
                }
            }

            var ids = new HashSet<int>();
            void Visit(FinancialReportAccountNode account, bool inherited)
            {
                var cost = inherited || CostName.IsMatch(account.AccountName ?? "");
                if (cost)
                    ids.Add(account.Id);
                if (!children.TryGetValue(account.Id, out var kids))
                    return;
                foreach (var child in kids)
                    Visit(child, cost);
            }
            foreach (var root in roots)
                Visit(root, inherited: false);
            return ids;
        }

        /// <summary>Round to cents the way the reports page totals the statement.</summary>
        public static decimal Cents(decimal amount)
        {
            var minor = decimal.Round(Math.Abs(amount) * 100m, 0, MidpointRounding.AwayFromZero);
            return amount < 0m ? -minor / 100m : minor / 100m;
        }

        private static (string Title, string? Subtitle) Titles(FinancialReportKind kind, bool personal) => kind switch
        {
            FinancialReportKind.TrialBalance => ("Trial Balance", "Accounts at net debit or credit"),
            FinancialReportKind.IncomeStatement => personal
                ? ("Money in and out", null)
                : ("Profit and Loss", "Income statement"),
            FinancialReportKind.BalanceSheet => personal
                ? ("Net worth (book value)", null)
                : ("Balance Sheet", "Statement of financial position"),
            FinancialReportKind.CashFlow => personal
                ? ("Cash movement", "Cash and bank accounts")
                : ("Statement of Cash Flows", "Indirect method"),
            _ => ("Financial report", null),
        };

        private static string? Footnote(FinancialReportKind kind, bool personal, bool movement)
        {
            if (movement)
                return "Lists cash and bank movements in the period. Opening cash is the balance before the start date.";
            return kind switch
            {
                FinancialReportKind.TrialBalance => "Each account is shown at its net debit or credit for postings in the selected period. Amounts posted before the start date are not carried forward.",
                FinancialReportKind.BalanceSheet => personal
                    ? "Book value through the as-of date, including opening balances and unclosed results."
                    : "Cumulative balances through the as-of date, including opening balances and unclosed earnings. Assets equal liabilities and equity when the row Difference is zero.",
                FinancialReportKind.CashFlow => "Indirect method. The three sections reconcile to the change in cash and cash equivalents. A Difference other than zero means the statement does not tie to the cash accounts.",
                FinancialReportKind.IncomeStatement => personal
                    ? null
                    : "Revenue minus cost of goods sold is gross profit. Operating expenses are the remaining costs. Net profit is gross profit minus operating expenses.",
                _ => null,
            };
        }

        private static string PeriodLabel(FinancialReportKind kind, DateTime? from, DateTime? to, string currency)
        {
            if (kind == FinancialReportKind.BalanceSheet)
                return to.HasValue ? "As of " + FormatDate(to.Value, currency) : "As of the latest posting";
            if (from.HasValue && to.HasValue)
                return FormatDate(from.Value, currency) + " to " + FormatDate(to.Value, currency);
            if (to.HasValue)
                return "All activity through " + FormatDate(to.Value, currency);
            if (from.HasValue)
                return "From " + FormatDate(from.Value, currency);
            return "All activity";
        }

        private static string FormatDate(DateTime value, string currency)
        {
            var date = value.Date;
            var culture = currency is "USD" or "CAD" ? CultureInfo.GetCultureInfo("en-US") : CultureInfo.GetCultureInfo("en-GB");
            return date.ToString(currency is "USD" or "CAD" ? "MMMM d, yyyy" : "d MMMM yyyy", culture);
        }

        private static string FormatStatusAmount(decimal amount, string currency)
        {
            var symbol = currency switch
            {
                "USD" => "$",
                "EUR" => "€",
                "GBP" => "£",
                "CAD" => "CA$",
                "AUD" => "AU$",
                "SGD" => "S$",
                _ => currency + " ",
            };
            return symbol + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);
        }

        private static string HeadingName(string? legal, string? trading)
        {
            if (!string.IsNullOrWhiteSpace(legal))
                return legal.Trim();
            if (!string.IsNullOrWhiteSpace(trading))
                return trading.Trim();
            return "AccountGo Organization";
        }

        private static string? TradingAs(string? legal, string? trading)
        {
            if (string.IsNullOrWhiteSpace(legal) || string.IsNullOrWhiteSpace(trading))
                return null;
            return string.Equals(legal.Trim(), trading.Trim(), StringComparison.OrdinalIgnoreCase) ? null : trading.Trim();
        }

        private static string FileStem(FinancialReportRequest request, bool personal)
        {
            var kind = request.Kind switch
            {
                FinancialReportKind.TrialBalance => "trial-balance",
                FinancialReportKind.IncomeStatement => personal ? "money-in-and-out" : "profit-and-loss",
                FinancialReportKind.BalanceSheet => personal ? "net-worth" : "balance-sheet",
                FinancialReportKind.CashFlow => personal ? "cash-movement" : "cash-flow",
                _ => "report",
            };
            var basis = request.Basis == AccountingBasis.Cash ? "cash" : "accrual";
            var date = (request.To ?? request.From)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "all";
            var slug = string.IsNullOrWhiteSpace(request.Slug) ? "workspace" : request.Slug.Trim();
            return string.Join("-", new[] { Slug(slug), kind, basis, date }.Where(part => part.Length > 0));
        }

        private static string Slug(string value)
        {
            var chars = value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
            var text = new string(chars);
            while (text.Contains("--", StringComparison.Ordinal))
                text = text.Replace("--", "-", StringComparison.Ordinal);
            return text.Trim('-');
        }

        private static string FormatGenerated(DateTime utc, string? timeZoneId)
        {
            var stamp = utc == default ? DateTime.UtcNow : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                try
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    var local = TimeZoneInfo.ConvertTimeFromUtc(stamp, zone);
                    return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + zone.Id;
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }
            return stamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        }

        private sealed class WorkLine
        {
            public string Code { get; init; } = "";
            public string Label { get; init; } = "";
            public FinancialReportLineKind Kind { get; init; }
            public int? AccountId { get; init; }
            public decimal? Debit { get; init; }
            public decimal? Credit { get; init; }
            public decimal? Amount { get; init; }
        }

        private sealed class NaturalCodeComparer : IComparer<string>
        {
            public static readonly NaturalCodeComparer Instance = new();

            public int Compare(string? left, string? right)
            {
                var a = left ?? "";
                var b = right ?? "";
                var ia = 0;
                var ib = 0;
                while (ia < a.Length && ib < b.Length)
                {
                    var aDigit = char.IsDigit(a[ia]);
                    var bDigit = char.IsDigit(b[ib]);
                    if (aDigit && bDigit)
                    {
                        var aStart = ia;
                        var bStart = ib;
                        while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                        while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                        var aNum = a.AsSpan(aStart, ia - aStart).TrimStart('0');
                        var bNum = b.AsSpan(bStart, ib - bStart).TrimStart('0');
                        if (aNum.IsEmpty) aNum = "0";
                        if (bNum.IsEmpty) bNum = "0";
                        var byLength = aNum.Length.CompareTo(bNum.Length);
                        if (byLength != 0) return byLength;
                        var byDigits = aNum.CompareTo(bNum, StringComparison.Ordinal);
                        if (byDigits != 0) return byDigits;
                        continue;
                    }
                    var cmp = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                    if (cmp != 0) return cmp;
                    ia++;
                    ib++;
                }
                return (a.Length - ia).CompareTo(b.Length - ib);
            }
        }
    }
}

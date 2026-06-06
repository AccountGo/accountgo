using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Dto.Dashboard;
using Services.Financial;
using Services.Purchasing;
using Services.Sales;

namespace Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;

        public DashboardService(
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService)
        {
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
        }

        public DashboardSummaryDto GetDashboardSummary(
            DateTime? from = null,
            DateTime? to = null,
            int topReceivables = 5,
            int topPayables = 5,
            int recentActivity = 5)
        {
            var (rangeStart, rangeEnd) = ResolveDateRange(from, to);

            var accounts = _financialService.GetAccounts().ToList();
            var ledger = _financialService.MasterGeneralLedger(rangeStart, rangeEnd).ToList();
            var balanceSheet = _financialService.BalanceSheet().ToList();
            var customers = _salesService.GetCustomers().ToList();
            var vendors = _purchasingService.GetVendors().ToList();
            var salesInvoices = _salesService.GetSalesInvoices().ToList();
            var salesReceipts = _salesService.GetSalesReceipts().ToList();
            var purchaseInvoices = _purchasingService.GetPurchaseInvoices().ToList();

            var revenueAccountIds = accounts
                .Where(a => a.AccountClassId == (int)AccountClasses.Revenue)
                .Select(a => a.Id)
                .ToHashSet();

            var expenseAccountIds = accounts
                .Where(a => a.AccountClassId == (int)AccountClasses.Expense)
                .Select(a => a.Id)
                .ToHashSet();

            var bankAccountIds = _financialService.GetCashAndBanks()
                .Where(b => b.AccountId.HasValue)
                .Select(b => b.AccountId!.Value)
                .Distinct()
                .ToHashSet();

            var totalSalesYtd = ComputeNetAmount(ledger, revenueAccountIds, normalBalanceIsDebit: false);
            var totalExpensesYtd = ComputeNetAmount(ledger, expenseAccountIds, normalBalanceIsDebit: true);
            var cashFlowYtd = ComputeNetAmount(ledger, bankAccountIds, normalBalanceIsDebit: true);

            var summary = new DashboardSummaryDto
            {
                Kpis = new DashboardKpiDto
                {
                    MoneyInBank = bankAccountIds.Sum(accountId => _financialService.GetAccount(accountId)?.Balance ?? 0m),
                    OutstandingReceivables = customers.Sum(c => c.Balance),
                    TotalPayables = vendors.Sum(v => v.GetBalance()),
                    TotalAssets = balanceSheet
                        .Where(item => item.AccountClassId == (int)AccountClasses.Assets)
                        .Sum(item => item.Amount),
                    TotalSalesYtd = totalSalesYtd,
                    TotalExpensesYtd = totalExpensesYtd,
                    NetProfitYtd = totalSalesYtd - totalExpensesYtd,
                    CashFlowYtd = cashFlowYtd
                },
                BalanceSnapshot = new DashboardBalanceSnapshotDto
                {
                    TotalAssets = balanceSheet
                        .Where(item => item.AccountClassId == (int)AccountClasses.Assets)
                        .Sum(item => item.Amount),
                    TotalLiabilities = balanceSheet
                        .Where(item => item.AccountClassId == (int)AccountClasses.Liabilities)
                        .Sum(item => item.Amount),
                    TotalEquity = balanceSheet
                        .Where(item => item.AccountClassId == (int)AccountClasses.Equity)
                        .Sum(item => item.Amount)
                },
                MonthlySales = BuildMonthlyTrend(
                    rangeStart,
                    rangeEnd,
                    ledger,
                    revenueAccountIds,
                    normalBalanceIsDebit: false),
                MonthlyExpenses = BuildMonthlyTrend(
                    rangeStart,
                    rangeEnd,
                    ledger,
                    expenseAccountIds,
                    normalBalanceIsDebit: true),
                TopReceivables = customers
                    .Where(c => c.Balance > 0)
                    .OrderByDescending(c => c.Balance)
                    .Take(topReceivables)
                    .Select(c => new DashboardReceivableItemDto
                    {
                        CustomerId = c.Id,
                        CustomerName = c.Party?.Name ?? c.No ?? $"Customer {c.Id}",
                        Balance = c.Balance
                    })
                    .ToList(),
                UpcomingPayables = purchaseInvoices
                    .Select(invoice => new
                    {
                        Invoice = invoice,
                        Remaining = invoice.PurchaseInvoiceLines.Sum(line => line.Amount * line.Quantity) - invoice.AmountPaid()
                    })
                    .Where(x => x.Remaining > 0)
                    .OrderBy(x => x.Invoice.Date)
                    .Take(topPayables)
                    .Select(x => new DashboardPayableItemDto
                    {
                        PurchaseInvoiceId = x.Invoice.Id,
                        VendorName = x.Invoice.Vendor?.Party?.Name ?? $"Vendor {x.Invoice.VendorId}",
                        InvoiceNo = x.Invoice.No ?? x.Invoice.VendorInvoiceNo ?? string.Empty,
                        RemainingAmount = x.Remaining,
                        InvoiceDate = x.Invoice.Date
                    })
                    .ToList(),
                RecentActivity = BuildRecentActivity(
                    salesInvoices,
                    salesReceipts,
                    purchaseInvoices,
                    recentActivity)
            };

            return summary;
        }

        private static (DateTime Start, DateTime End) ResolveDateRange(DateTime? from, DateTime? to)
        {
            var today = DateTime.Today;
            var start = from?.Date ?? new DateTime(today.Year, 1, 1);
            var end = to?.Date ?? today;

            if (end < start)
            {
                (start, end) = (end, start);
            }

            return (start, end);
        }

        private List<DashboardTrendPointDto> BuildMonthlyTrend(
            DateTime rangeStart,
            DateTime rangeEnd,
            IEnumerable<MasterGeneralLedger> ledger,
            ISet<int> accountIds,
            bool normalBalanceIsDebit)
        {
            var monthlyAmounts = ledger
                .Where(line => accountIds.Contains(line.AccountId))
                .GroupBy(line => new { line.Date.Year, line.Date.Month })
                .ToDictionary(
                    group => new DateTime(group.Key.Year, group.Key.Month, 1),
                    group => normalBalanceIsDebit
                        ? group.Sum(item => item.Debit) - group.Sum(item => item.Credit)
                        : group.Sum(item => item.Credit) - group.Sum(item => item.Debit));

            var trend = new List<DashboardTrendPointDto>();
            var cursor = new DateTime(rangeStart.Year, rangeStart.Month, 1);
            var endMonth = new DateTime(rangeEnd.Year, rangeEnd.Month, 1);

            while (cursor <= endMonth)
            {
                monthlyAmounts.TryGetValue(cursor, out var amount);

                trend.Add(new DashboardTrendPointDto
                {
                    Label = cursor.ToString("MMM", CultureInfo.InvariantCulture),
                    Amount = amount
                });

                cursor = cursor.AddMonths(1);
            }

            return trend;
        }

        private static decimal ComputeNetAmount(
            IEnumerable<MasterGeneralLedger> ledger,
            ISet<int> accountIds,
            bool normalBalanceIsDebit)
        {
            var filtered = ledger.Where(line => accountIds.Contains(line.AccountId));

            return normalBalanceIsDebit
                ? filtered.Sum(line => line.Debit) - filtered.Sum(line => line.Credit)
                : filtered.Sum(line => line.Credit) - filtered.Sum(line => line.Debit);
        }

        private static List<DashboardActivityItemDto> BuildRecentActivity(
            IEnumerable<Core.Domain.Sales.SalesInvoiceHeader> salesInvoices,
            IEnumerable<Core.Domain.Sales.SalesReceiptHeader> salesReceipts,
            IEnumerable<Core.Domain.Purchases.PurchaseInvoiceHeader> purchaseInvoices,
            int recentActivity)
        {
            var items = new List<DashboardActivityItemDto>();

            items.AddRange(salesInvoices.Select(invoice => new DashboardActivityItemDto
            {
                Type = "Sales Invoice",
                Description = $"Sales invoice {invoice.No} for {invoice.Customer?.Party?.Name ?? "customer"}",
                Date = invoice.Date
            }));

            items.AddRange(salesReceipts.Select(receipt => new DashboardActivityItemDto
            {
                Type = "Sales Receipt",
                Description = $"Payment received on receipt {receipt.No}",
                Date = receipt.Date
            }));

            items.AddRange(purchaseInvoices.Select(invoice => new DashboardActivityItemDto
            {
                Type = "Purchase Invoice",
                Description = $"Purchase invoice {invoice.No} from {invoice.Vendor?.Party?.Name ?? "vendor"}",
                Date = invoice.Date
            }));

            return items
                .OrderByDescending(item => item.Date)
                .Take(recentActivity)
                .ToList();
        }
    }
}

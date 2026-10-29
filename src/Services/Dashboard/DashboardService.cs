using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Dashboard;
using Services.Banking;
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
        private readonly IBankAccountService _bankAccountService;
        private readonly ITenantContext _tenantContext;

        public DashboardService(
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IBankAccountService bankAccountService,
            ITenantContext tenantContext)
        {
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _bankAccountService = bankAccountService;
            _tenantContext = tenantContext;
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

            // Workspaces without bank records still hold cash in the postable cash accounts (e.g. 10111).
            var bankAccountIds = _financialService.GetCashAndBanks()
                .Where(b => b.AccountId.HasValue)
                .Select(b => b.AccountId!.Value)
                .Concat(accounts
                    .Where(a => a.CanPost() && (a.IsCash || (a.ParentAccount != null && a.ParentAccount.IsCash)))
                    .Select(a => a.Id))
                .ToHashSet();

            var totalSalesYtd = ComputeNetAmount(ledger, revenueAccountIds, normalBalanceIsDebit: false);
            var totalExpensesYtd = ComputeNetAmount(ledger, expenseAccountIds, normalBalanceIsDebit: true);
            var cashFlowYtd = ComputeNetAmount(ledger, bankAccountIds, normalBalanceIsDebit: true);

            var summary = new DashboardSummaryDto
            {
                Kpis = new DashboardKpiDto
                {
                    MoneyInBank = accounts.Where(a => bankAccountIds.Contains(a.Id)).Sum(a => a.Balance),
                    OutstandingReceivables = customers.Sum(c => c.Balance),
                    TotalPayables = purchaseInvoices.Where(i => i.IsPayable).Sum(i => i.BalanceDue()),
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
                // Approved bills still owed, soonest due first.
                UpcomingPayables = purchaseInvoices
                    .Where(invoice => invoice.IsPayable && invoice.BalanceDue() > 0)
                    .OrderBy(invoice => invoice.DueDate ?? invoice.Date)
                    .Take(topPayables)
                    .Select(invoice => new DashboardPayableItemDto
                    {
                        PurchaseInvoiceId = invoice.Id,
                        VendorName = invoice.Vendor?.Party?.Name ?? $"Vendor {invoice.VendorId}",
                        InvoiceNo = string.IsNullOrWhiteSpace(invoice.VendorInvoiceNo) ? invoice.No ?? string.Empty : invoice.VendorInvoiceNo,
                        RemainingAmount = invoice.BalanceDue(),
                        InvoiceDate = invoice.Date
                    })
                    .ToList(),
                RecentActivity = BuildRecentActivity(
                    salesInvoices,
                    salesReceipts,
                    purchaseInvoices,
                    recentActivity),
                BankCloseStatus = BuildBankCloseStatus(DateTime.Today)
            };

            return summary;
        }

        private Dto.Banking.BankCloseStatusDto? BuildBankCloseStatus(DateTime asOf)
        {
            if (!_tenantContext.IsResolved
                || !_tenantContext.Permissions.Contains(SystemPermissions.BankingAccountsView))
            {
                return null;
            }

            return _bankAccountService.GetCloseStatus(asOf);
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

            items.AddRange(purchaseInvoices.Where(invoice => !invoice.IsDraft).Select(invoice => new DashboardActivityItemDto
            {
                Type = "Purchase Invoice",
                Description = $"Bill {invoice.No} from {invoice.Vendor?.Party?.Name ?? "vendor"}",
                Date = invoice.Date
            }));

            return items
                .OrderByDescending(item => item.Date)
                .Take(recentActivity)
                .ToList();
        }
    }
}

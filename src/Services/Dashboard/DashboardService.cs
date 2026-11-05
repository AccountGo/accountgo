using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Dashboard;
using Core.Domain.Sales;
using Services.Banking;
using Services.Financial;
using Services.Purchasing;
using Services.Sales;

namespace Services.Dashboard
{
    public class DashboardService : IDashboardService
    {
        private const int MoneyDecimalPlaces = 2;

        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IBankAccountService _bankAccountService;
        private readonly ITenantContext _tenantContext;
        private readonly IAccessAuthorizationService? _authorizationService;

        public DashboardService(
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IBankAccountService bankAccountService,
            ITenantContext tenantContext,
            IAccessAuthorizationService? authorizationService = null)
        {
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _bankAccountService = bankAccountService;
            _tenantContext = tenantContext;
            _authorizationService = authorizationService;
        }

        public DashboardSummaryDto GetDashboardSummary(
            DateTime? from = null,
            DateTime? to = null,
            int topReceivables = 5,
            int topPayables = 5,
            int recentActivity = 5)
        {
            var (rangeStart, rangeEnd) = ResolveDateRange(from, to);
            var (canViewFinancials, canViewSales, canViewPurchases) = ResolveAuthorizedDomains();
            var canViewBanks = CanViewBankAccounts();

            // Ledger accounts only when financials is authorized — never use GetAccounts() to price banks
            // for an invoicing caller who only has banking (that call returns an empty set under auth).
            var accounts = canViewFinancials
                ? (_financialService.GetAccounts()?.ToList() ?? new List<Account>())
                : new List<Account>();
            var ledger = canViewFinancials
                ? (_financialService.MasterGeneralLedger(rangeStart, rangeEnd)?.ToList() ?? new List<MasterGeneralLedger>())
                : new List<MasterGeneralLedger>();
            var balanceSheet = canViewFinancials
                ? (_financialService.BalanceSheet()?.ToList() ?? new List<BalanceSheet>())
                : new List<BalanceSheet>();
            var customers = canViewSales
                ? (_salesService.GetCustomers()?.ToList() ?? new List<Customer>())
                : new List<Customer>();
            var salesInvoices = canViewSales
                ? (_salesService.GetSalesInvoices()?.ToList() ?? new List<SalesInvoiceHeader>())
                : new List<SalesInvoiceHeader>();
            var salesReceipts = canViewSales
                ? (_salesService.GetSalesReceipts()?.ToList() ?? new List<SalesReceiptHeader>())
                : new List<SalesReceiptHeader>();
            var purchaseInvoices = canViewPurchases
                ? (_purchasingService.GetPurchaseInvoices()?.ToList() ?? new List<Core.Domain.Purchases.PurchaseInvoiceHeader>())
                : new List<Core.Domain.Purchases.PurchaseInvoiceHeader>();

            var revenueAccountIds = accounts
                .Where(a => a.AccountClassId == (int)AccountClasses.Revenue)
                .Select(a => a.Id)
                .ToHashSet();

            var expenseAccountIds = accounts
                .Where(a => a.AccountClassId == (int)AccountClasses.Expense)
                .Select(a => a.Id)
                .ToHashSet();

            var freelanceIncomeAccountIds = accounts
                .Where(a => a.AccountCode == "40900")
                .Select(a => a.Id)
                .ToHashSet();
            var drawingsAccountIds = accounts
                .Where(a => a.AccountCode == "30800")
                .Select(a => a.Id)
                .ToHashSet();

            // Workspaces without bank records still hold cash in the postable cash accounts (e.g. 10111).
            var bankAccountIds = canViewFinancials
                ? (_financialService.GetCashAndBanks() ?? Enumerable.Empty<Bank>())
                    .Where(b => b.AccountId.HasValue)
                    .Select(b => b.AccountId!.Value)
                    .Concat(accounts
                        .Where(a => a.CanPost() && (a.IsCash || (a.ParentAccount != null && a.ParentAccount.IsCash)))
                        .Select(a => a.Id))
                    .ToHashSet()
                : new HashSet<int>();

            // Prefer the banking service (authorized book balances) whenever banking is allowed.
            // Fall back to GetAccounts()-based totals only for financials viewers without banking.
            var moneyInBankAvailable = canViewBanks || canViewFinancials;
            var moneyInBank = canViewBanks
                ? BuildMoneyInBankFromBankingService()
                : canViewFinancials
                    ? BuildMoneyInBankFromAccounts(accounts)
                    : (Total: 0m, BankBalances: (List<DashboardBankBalanceDto>?)null, UnassignedCash: 0m);

            // IncomeStatement excludes ClosingEntries so YTD is unchanged after year-end close.
            // Invoicing workspaces without financials use issued invoices for sales YTD instead.
            var incomeItems = canViewFinancials
                ? (_financialService.IncomeStatement(rangeStart, rangeEnd) ?? Enumerable.Empty<IncomeStatement>()).ToList()
                : new List<IncomeStatement>();
            decimal totalSalesYtd;
            decimal totalExpensesYtd;
            if (canViewFinancials)
            {
                totalSalesYtd = RoundMoney(incomeItems.Where(i => !i.IsExpense).Sum(i => i.Amount));
                totalExpensesYtd = RoundMoney(incomeItems.Where(i => i.IsExpense).Sum(i => i.Amount));
            }
            else if (canViewSales)
            {
                totalSalesYtd = RoundMoney(IssuedSalesInRange(salesInvoices, rangeStart, rangeEnd)
                    .Sum(i => i.GrandTotal()));
                totalExpensesYtd = 0m;
            }
            else
            {
                totalSalesYtd = 0m;
                totalExpensesYtd = 0m;
            }

            var cashFlowYtd = canViewFinancials
                ? ComputeNetAmount(ledger, bankAccountIds, normalBalanceIsDebit: true)
                : 0m;
            var moneyFromFreelanceWorkYtd = canViewFinancials
                ? RoundMoney(ComputeNetAmount(ledger, freelanceIncomeAccountIds, normalBalanceIsDebit: false))
                : 0m;
            var earnedIncomeYtd = canViewFinancials
                ? RoundMoney(totalSalesYtd - moneyFromFreelanceWorkYtd)
                : totalSalesYtd;
            var drawingsYtd = canViewFinancials
                ? RoundMoney(ComputeNetAmount(ledger, drawingsAccountIds, normalBalanceIsDebit: true))
                : 0m;

            var today = DateTime.Today;
            var openSales = canViewSales
                ? salesInvoices.Where(IsOpenSalesInvoice).ToList()
                : new List<SalesInvoiceHeader>();
            var overdueSales = openSales
                .Where(i => i.ResolveDueDate(null).Date < today)
                .ToList();

            // Gross open A/R uses the same BalanceDue() basis as overdue (tax, shipping, discount, allocations).
            var outstandingReceivables = RoundMoney(openSales.Sum(i => i.BalanceDue()));
            var overdueReceivables = RoundMoney(overdueSales.Sum(i => i.BalanceDue()));
            var totalPayables = canViewPurchases
                ? RoundMoney(purchaseInvoices.Where(i => i.IsPayable).Sum(i => i.BalanceDue()))
                : 0m;

            var customersById = customers.ToDictionary(c => c.Id);
            var topReceivableRows = openSales
                .GroupBy(i => i.CustomerId)
                .Select(group =>
                {
                    customersById.TryGetValue(group.Key, out var customer);
                    return new DashboardReceivableItemDto
                    {
                        CustomerId = group.Key,
                        CustomerName = customer?.Party?.Name ?? customer?.No ?? $"Customer {group.Key}",
                        Balance = RoundMoney(group.Sum(i => i.BalanceDue()))
                    };
                })
                .Where(row => row.Balance > 0)
                .OrderByDescending(row => row.Balance)
                .Take(topReceivables)
                .ToList();

            var summary = new DashboardSummaryDto
            {
                Surface = canViewFinancials ? "Accounting" : "Invoicing",
                Capabilities = new DashboardCapabilitiesDto
                {
                    Financials = canViewFinancials,
                    Sales = canViewSales,
                    Purchases = canViewPurchases,
                    Banking = canViewBanks,
                    MoneyInBank = moneyInBankAvailable,
                    Receivables = canViewSales,
                    Payables = canViewPurchases
                },
                Kpis = new DashboardKpiDto
                {
                    MoneyInBank = moneyInBank.Total,
                    BankBalances = moneyInBank.BankBalances,
                    UnassignedCash = moneyInBank.UnassignedCash,
                    OutstandingReceivables = outstandingReceivables,
                    TotalPayables = totalPayables,
                    TotalAssets = balanceSheet
                        .Where(item => item.AccountClassId == (int)AccountClasses.Assets)
                        .Sum(item => item.Amount),
                    TotalSalesYtd = totalSalesYtd,
                    EarnedIncomeYtd = earnedIncomeYtd,
                    MoneyFromFreelanceWorkYtd = moneyFromFreelanceWorkYtd,
                    DrawingsYtd = drawingsYtd,
                    TotalExpensesYtd = totalExpensesYtd,
                    NetProfitYtd = canViewFinancials ? totalSalesYtd - totalExpensesYtd : 0m,
                    CashFlowYtd = cashFlowYtd,
                    OverdueReceivables = overdueReceivables,
                    OpenInvoiceCount = openSales.Count,
                    OverdueInvoiceCount = overdueSales.Count
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
                MonthlySales = canViewFinancials
                    ? BuildMonthlyTrend(
                        rangeStart,
                        rangeEnd,
                        ledger,
                        revenueAccountIds,
                        normalBalanceIsDebit: false)
                    : canViewSales
                        ? BuildMonthlySalesFromInvoices(rangeStart, rangeEnd, salesInvoices)
                        : new List<DashboardTrendPointDto>(),
                MonthlyExpenses = canViewFinancials
                    ? BuildMonthlyTrend(
                        rangeStart,
                        rangeEnd,
                        ledger,
                        expenseAccountIds,
                        normalBalanceIsDebit: true)
                    : new List<DashboardTrendPointDto>(),
                TopReceivables = topReceivableRows,
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

        private static bool IsOpenSalesInvoice(Core.Domain.Sales.SalesInvoiceHeader invoice) =>
            invoice.Status is not (SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
            && invoice.BalanceDue() > 0.005m;

        private static IEnumerable<Core.Domain.Sales.SalesInvoiceHeader> IssuedSalesInRange(
            IEnumerable<Core.Domain.Sales.SalesInvoiceHeader> invoices,
            DateTime rangeStart,
            DateTime rangeEnd) =>
            invoices.Where(invoice =>
                invoice.Status is not (SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void)
                && invoice.Date.Date >= rangeStart.Date
                && invoice.Date.Date <= rangeEnd.Date);

        private List<DashboardTrendPointDto> BuildMonthlySalesFromInvoices(
            DateTime rangeStart,
            DateTime rangeEnd,
            IEnumerable<Core.Domain.Sales.SalesInvoiceHeader> invoices)
        {
            var monthlyAmounts = IssuedSalesInRange(invoices, rangeStart, rangeEnd)
                .GroupBy(invoice => new { invoice.Date.Year, invoice.Date.Month })
                .ToDictionary(
                    group => new DateTime(group.Key.Year, group.Key.Month, 1),
                    group => RoundMoney(group.Sum(i => i.GrandTotal())));

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

        private (bool CanViewFinancials, bool CanViewSales, bool CanViewPurchases) ResolveAuthorizedDomains()
        {
            if (_authorizationService != null)
            {
                var domains = _authorizationService.GetAuthorizedDashboardDomains();
                var financials = domains.Contains("financials");
                var sales = domains.Contains("sales");
                var purchases = domains.Contains("purchases");
                return (financials, sales, purchases);
            }

            return (false, false, false);
        }

        private bool CanViewBankAccounts()
        {
            if (_authorizationService != null)
            {
                return _authorizationService.GetAuthorizedDashboardDomains().Contains("banking");
            }

            return false;
        }

        /// <summary>
        /// Banking-authorized money-in-bank: book balances from <see cref="IBankAccountService.List"/>,
        /// which does not require financials/report access to read bank account balances.
        /// </summary>
        private (decimal Total, List<DashboardBankBalanceDto>? BankBalances, decimal UnassignedCash) BuildMoneyInBankFromBankingService()
        {
            var list = _bankAccountService.List(
                includeArchived: true,
                includeFullNumbers: false,
                includeCashHistoryCodes: false);

            var balances = list.Accounts
                .Select(b => new DashboardBankBalanceDto
                {
                    BankId = b.Id,
                    Name = b.Name,
                    IsArchived = b.ArchivedAt != null,
                    Balance = RoundMoney(b.BookBalance)
                })
                .Where(b => !b.IsArchived || b.Balance != 0m)
                .OrderBy(b => b.IsArchived)
                .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var unassignedCash = RoundMoney(list.UnassignedCash.Sum(u => u.Balance));
            var total = balances.Sum(b => b.Balance) + unassignedCash;
            return (total, balances, unassignedCash);
        }

        /// <summary>
        /// Financials-authorized money-in-bank from chart accounts (callers without the banking domain).
        /// An archived bank is listed only while its account still holds a balance.
        /// </summary>
        private (decimal Total, List<DashboardBankBalanceDto>? BankBalances, decimal UnassignedCash) BuildMoneyInBankFromAccounts(
            IReadOnlyCollection<Account> accounts)
        {
            var allBanks = _financialService.GetCashAndBanks().ToList();
            var accountById = accounts.ToDictionary(a => a.Id);

            var balances = allBanks
                .Where(b => b.AccountId.HasValue)
                .Select(b => new DashboardBankBalanceDto
                {
                    BankId = b.Id,
                    Name = b.Name,
                    IsArchived = b.ArchivedAt != null,
                    Balance = RoundMoney(accountById.TryGetValue(b.AccountId!.Value, out var account) ? account.Balance : 0m)
                })
                .Where(b => !b.IsArchived || b.Balance != 0m)
                .OrderBy(b => b.IsArchived)
                .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var accountsWithBankRecord = allBanks
                .Where(b => b.AccountId.HasValue)
                .Select(b => b.AccountId!.Value)
                .ToHashSet();
            var unassignedCash = RoundMoney(accounts
                .Where(a => a.CanPost() && (a.IsCash || (a.ParentAccount != null && a.ParentAccount.IsCash)))
                .Where(a => !accountsWithBankRecord.Contains(a.Id))
                .Sum(a => a.Balance));

            var total = balances.Sum(b => b.Balance) + unassignedCash;
            // BankBalances detail is only returned when the banking domain is authorized.
            return (total, CanViewBankAccounts() ? balances : null, unassignedCash);
        }

        private static decimal RoundMoney(decimal amount) =>
            Math.Round(amount, MoneyDecimalPlaces, MidpointRounding.AwayFromZero);

        private Dto.Banking.BankCloseStatusDto? BuildBankCloseStatus(DateTime asOf)
        {
            if (!CanViewBankAccounts())
                return null;

            return _bankAccountService.GetCloseStatus(asOf);
        }

        private (DateTime Start, DateTime End) ResolveDateRange(DateTime? from, DateTime? to)
        {
            var today = DateTime.Today;
            var start = from?.Date
                ?? _financialService.CurrentFiscalYear()?.StartDate.Date
                ?? new DateTime(today.Year, 1, 1);
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

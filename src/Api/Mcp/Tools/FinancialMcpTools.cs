#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Mcp.Models;
using Core.Domain;
using Core.Domain.Security;
using Core.Tenancy;
using ModelContextProtocol.Server;
using Services.Administration;
using Services.Financial;

namespace Api.Mcp.Tools
{
    [McpServerToolType]
    public class FinancialMcpTools
    {
        private readonly IFinancialService _financialService;
        private readonly IAdministrationService _adminService;
        private readonly ITenantContext _tenantContext;

        public FinancialMcpTools(
            IFinancialService financialService,
            IAdministrationService adminService,
            ITenantContext tenantContext)
        {
            _financialService = financialService ?? throw new ArgumentNullException(nameof(financialService));
            _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        }

        #region Phase 1: Chart of Accounts

        [McpServerTool(
            Name = "get_chart_of_accounts",
            Title = "Get Chart of Accounts",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Retrieves the organization's Chart of Accounts (COA) including account codes, names, classifications (Asset, Liability, Equity, Revenue, Expense), normal balances, and current balances.")]
        public Task<ChartOfAccountsMcpResponse> GetChartOfAccounts(
            [Description("Optional search text to filter accounts by code or name.")] string? filter = null,
            [Description("Optional classification filter (e.g., 'Asset', 'Liability', 'Equity', 'Revenue', 'Expense').")] string? accountClass = null,
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            EnsureModule("financials", "reports");

            var allAccounts = _financialService.GetAccounts()?.ToList() ?? new List<Core.Domain.Financials.Account>();

            var query = allAccounts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(filter))
            {
                var term = filter.Trim();
                query = query.Where(a =>
                    (!string.IsNullOrEmpty(a.AccountCode) && a.AccountCode.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(a.AccountName) && a.AccountName.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(accountClass))
            {
                var targetClass = accountClass.Trim();
                query = query.Where(a =>
                    a.AccountClass != null &&
                    a.AccountClass.Name.Contains(targetClass, StringComparison.OrdinalIgnoreCase));
            }

            if (!includeZeroBalances)
            {
                query = query.Where(a => a.Balance != 0);
            }

            var filteredAccounts = query.OrderBy(a => a.AccountCode).ToList();

            var classificationCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in filteredAccounts)
            {
                var className = account.AccountClass?.Name ?? "Unclassified";
                classificationCounts[className] = classificationCounts.GetValueOrDefault(className, 0) + 1;
            }

            var response = new ChartOfAccountsMcpResponse
            {
                Organization = _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}",
                TotalAccounts = filteredAccounts.Count,
                TotalUnfilteredAccounts = allAccounts.Count,
                AsOfDateUtc = DateTime.UtcNow,
                ClassificationCounts = classificationCounts,
                Accounts = filteredAccounts.Select(a => new AccountMcpSummary
                {
                    Id = a.Id,
                    Code = a.AccountCode ?? string.Empty,
                    Name = a.AccountName ?? string.Empty,
                    Description = a.Description,
                    AccountClass = a.AccountClass?.Name ?? "Unknown",
                    NormalBalance = a.DrOrCrSide != DrOrCrSide.NA
                        ? (a.DrOrCrSide == DrOrCrSide.Cr ? "Cr" : "Dr")
                        : (a.AccountClass?.NormalBalance ?? "Dr"),
                    Balance = a.Balance,
                    DebitBalance = a.DebitBalance,
                    CreditBalance = a.CreditBalance,
                    IsCash = a.IsCash,
                    IsContraAccount = a.IsContraAccount,
                    ParentAccountCode = a.ParentAccount?.AccountCode,
                    CanPost = a.ChildAccounts == null || a.ChildAccounts.Count == 0
                }).ToList()
            };

            return Task.FromResult(response);
        }

        #endregion

        #region Phase 2: Financial Reports & Ledger Drilldown

        [McpServerTool(
            Name = "get_trial_balance",
            Title = "Get Trial Balance",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Generates a Trial Balance report for the organization across a date range and accounting basis (Accrual or Cash), summarizing total debit and credit balances per account.")]
        public Task<TrialBalanceMcpResponse> GetTrialBalance(
            [Description("Optional inclusive start date (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional inclusive end date (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            EnsureModule("financials", "reports");
            EnsurePermission(SystemPermissions.ReportsView);

            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate), inclusiveEndOfDay: true);
            ValidateDateRange(from, to);
            var basis = ParseBasis(accountingBasis);

            var rawItems = _financialService.TrialBalance(from, to, basis)?.ToList() ?? new List<TrialBalance>();

            var totalDebit = rawItems.Sum(i => i.Debit);
            var totalCredit = rawItems.Sum(i => i.Credit);

            var response = new TrialBalanceMcpResponse
            {
                Organization = _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}",
                AccountingBasis = basis.ToString(),
                FromDate = from,
                ToDate = to,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                IsBalanced = Math.Round(totalDebit, 2) == Math.Round(totalCredit, 2),
                Difference = Math.Abs(totalDebit - totalCredit),
                AccountCount = rawItems.Count,
                Items = rawItems.Select(i => new TrialBalanceItemMcp
                {
                    AccountId = i.AccountId,
                    AccountCode = i.AccountCode,
                    AccountName = i.AccountName,
                    Debit = i.Debit,
                    Credit = i.Credit
                }).ToList()
            };

            return Task.FromResult(response);
        }

        [McpServerTool(
            Name = "get_balance_sheet",
            Title = "Get Balance Sheet",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Generates a cumulative Balance Sheet statement (Assets, Liabilities, and Equity) as of a given date and accounting basis (Accrual or Cash).")]
        public Task<BalanceSheetMcpResponse> GetBalanceSheet(
            [Description("Optional cumulative as-of date (e.g., '2026-12-31'). Includes all activity up to and including this date.")] string? asOfDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            EnsureModule("financials", "reports");
            EnsurePermission(SystemPermissions.ReportsView);

            var to = ParseDate(asOfDate, nameof(asOfDate), inclusiveEndOfDay: true);
            var basis = ParseBasis(accountingBasis);

            var rawItems = _financialService.BalanceSheet(null, to, basis)?.ToList() ?? new List<BalanceSheet>();
            if (!includeZeroBalances)
            {
                rawItems = rawItems.Where(i => i.Amount != 0m).ToList();
            }

            // AccountClassId 1: Assets, 2: Liabilities, 3: Equity
            var totalAssets = rawItems.Where(i => i.AccountClassId == 1).Sum(i => i.Amount);
            var totalLiabilities = rawItems.Where(i => i.AccountClassId == 2).Sum(i => i.Amount);
            var totalEquity = rawItems.Where(i => i.AccountClassId == 3).Sum(i => i.Amount);

            var response = new BalanceSheetMcpResponse
            {
                Organization = _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}",
                AccountingBasis = basis.ToString(),
                AsOfDate = to,
                TotalAssets = totalAssets,
                TotalLiabilities = totalLiabilities,
                TotalEquity = totalEquity,
                NetAssets = totalAssets - totalLiabilities,
                IsBalanced = Math.Round(totalAssets, 2) == Math.Round(totalLiabilities + totalEquity, 2),
                Items = rawItems.Select(i => new BalanceSheetItemMcp
                {
                    AccountId = i.AccountId,
                    AccountClassId = i.AccountClassId,
                    AccountCode = i.AccountCode,
                    AccountName = i.AccountName,
                    Amount = i.Amount
                }).ToList()
            };

            return Task.FromResult(response);
        }

        [McpServerTool(
            Name = "get_income_statement",
            Title = "Get Income Statement (P&L)",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Generates an Income Statement (Profit & Loss) for the organization over a date range and accounting basis (Accrual or Cash), calculating total revenues, total expenses, and net income.")]
        public Task<IncomeStatementMcpResponse> GetIncomeStatement(
            [Description("Optional period start date (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional period end date (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            EnsureModule("financials", "reports");
            EnsurePermission(SystemPermissions.ReportsView);

            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate), inclusiveEndOfDay: true);
            ValidateDateRange(from, to);
            var basis = ParseBasis(accountingBasis);

            var rawItems = _financialService.IncomeStatement(from, to, basis)?.ToList() ?? new List<IncomeStatement>();
            if (!includeZeroBalances)
            {
                rawItems = rawItems.Where(i => i.Amount != 0m).ToList();
            }

            var totalRevenue = rawItems.Where(i => !i.IsExpense).Sum(i => i.Amount);
            var totalExpense = rawItems.Where(i => i.IsExpense).Sum(i => i.Amount);

            var response = new IncomeStatementMcpResponse
            {
                Organization = _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}",
                AccountingBasis = basis.ToString(),
                FromDate = from,
                ToDate = to,
                TotalRevenue = totalRevenue,
                TotalExpense = totalExpense,
                NetIncome = totalRevenue - totalExpense,
                Items = rawItems.Select(i => new IncomeStatementItemMcp
                {
                    AccountId = i.AccountId,
                    IsExpense = i.IsExpense,
                    AccountCode = i.AccountCode,
                    AccountName = i.AccountName,
                    Amount = i.Amount
                }).ToList()
            };

            return Task.FromResult(response);
        }

        [McpServerTool(
            Name = "get_general_ledger_transactions",
            Title = "Get General Ledger Transactions",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Retrieves a paged register of posted General Ledger journal transactions with complete lines (accounts, debits, credits, narrations, and document types).")]
        public Task<GeneralLedgerTransactionsMcpResponse> GetGeneralLedgerTransactions(
            [Description("Optional start date filter (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional end date filter (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Optional account code filter to list transactions touching this account.")] string? accountCode = null,
            [Description("Optional exact posted transaction number.")] int? transactionNo = null,
            [Description("Optional source document type (e.g., 'SalesInvoice', 'SalesReceipt', 'PurchaseInvoice', 'PurchaseInvoicePayment', 'JournalEntry', 'CustomerAllocation', 'InventoryAdjustment').")] string? documentType = null,
            [Description("Number of records to skip for pagination. Defaults to 0.")] int skip = 0,
            [Description("Number of records to take (1 to 500). Defaults to 50.")] int take = 50,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            EnsureModule("financials", "reports");
            EnsurePermission(SystemPermissions.FinancialsJournalView);

            if (skip < 0)
            {
                throw new ArgumentException("Skip must be greater than or equal to 0.", nameof(skip));
            }

            if (take < 1 || take > FinancialService.GeneralLedgerMaxPageSize)
            {
                throw new ArgumentException($"Take must be between 1 and {FinancialService.GeneralLedgerMaxPageSize}.", nameof(take));
            }

            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate), inclusiveEndOfDay: true);
            ValidateDateRange(from, to);

            DocumentTypes? parsedDocType = null;
            if (!string.IsNullOrWhiteSpace(documentType))
            {
                if (Enum.TryParse<DocumentTypes>(documentType.Trim(), true, out var dt) &&
                    Enum.IsDefined(typeof(DocumentTypes), dt))
                {
                    parsedDocType = dt;
                }
                else
                {
                    var validNames = string.Join(", ", Enum.GetNames(typeof(DocumentTypes)));
                    throw new ArgumentException($"Invalid document type '{documentType}'. Allowed values are: {validNames}.", nameof(documentType));
                }
            }

            var page = _financialService.GetGeneralLedgerTransactions(
                from, to,
                string.IsNullOrWhiteSpace(accountCode) ? null : accountCode.Trim(),
                transactionNo,
                parsedDocType,
                skip, take);

            var response = new GeneralLedgerTransactionsMcpResponse
            {
                Organization = _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}",
                TotalCount = page?.TotalCount ?? 0,
                Skip = page?.Skip ?? skip,
                Take = page?.Take ?? take,
                FromDate = from,
                ToDate = to,
                AccountCode = accountCode,
                Transactions = page?.Items ?? new List<Dto.Financial.GeneralLedgerTransaction>()
            };

            return Task.FromResult(response);
        }

        #endregion

        #region Helper Guards

        private void EnsureTenantResolved()
        {
            if (!_tenantContext.IsResolved || _tenantContext.CompanyId == null)
            {
                throw new InvalidOperationException("Tenant context is required. Ensure 'X-AccountGo-Org' header is supplied with an authorized organization slug.");
            }
        }

        private void EnsureModule(params string[] moduleKeys)
        {
            if (!Array.Exists(moduleKeys, _adminService.IsModuleEnabled))
            {
                throw new InvalidOperationException($"The organization's active subscription plan does not include the '{string.Join("' or '", moduleKeys)}' module required for this operation.");
            }
        }

        private void EnsurePermission(params string[] requiredPermissions)
        {
            if (_tenantContext.Role == CompanyMemberRole.Owner || _tenantContext.Role == CompanyMemberRole.Admin)
            {
                return;
            }

            foreach (var permission in requiredPermissions)
            {
                if (_tenantContext.Permissions.Contains(permission))
                {
                    return;
                }
            }

            throw new InvalidOperationException($"Access denied. You do not have the required permission ({string.Join(" or ", requiredPermissions)}) to access this resource.");
        }

        private static DateTime? ParseDate(string? dateStr, string paramName, bool inclusiveEndOfDay = false)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;
            if (DateTime.TryParse(dateStr.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                if (inclusiveEndOfDay && parsed.TimeOfDay == TimeSpan.Zero)
                {
                    return parsed.Date.AddDays(1).AddTicks(-1);
                }
                return parsed;
            }
            throw new ArgumentException($"Invalid date format for '{paramName}'. Provide a valid ISO-8601 date string (e.g., '2026-01-31').", paramName);
        }

        private static void ValidateDateRange(DateTime? from, DateTime? to)
        {
            if (from.HasValue && to.HasValue && from.Value > to.Value)
            {
                throw new ArgumentException("Start date must not be after end date.");
            }
        }

        private static AccountingBasis ParseBasis(string? basisStr)
        {
            if (string.IsNullOrWhiteSpace(basisStr)) return AccountingBasis.Accrual;
            if (Enum.TryParse<AccountingBasis>(basisStr.Trim(), true, out var basis) &&
                Enum.IsDefined(typeof(AccountingBasis), basis))
            {
                return basis;
            }
            throw new ArgumentException($"Invalid accounting basis '{basisStr}'. Allowed values are 'Accrual' or 'Cash'.", nameof(basisStr));
        }

        #endregion
    }
}

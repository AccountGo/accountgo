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
using Core.Entitlements;
using Core.Tenancy;
using Dto.Subscription;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Services.Administration;
using Services.Financial;

namespace Api.Mcp.Tools
{
    [McpServerToolType]
    public class FinancialMcpTools
    {
        private static readonly string[] FinancialOrReportsModules = { "financials", "reports" };

        private readonly IFinancialService _financialService;
        private readonly IAdministrationService _adminService;
        private readonly IEntitlementService _entitlements;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<FinancialMcpTools> _logger;

        public FinancialMcpTools(
            IFinancialService financialService,
            IAdministrationService adminService,
            IEntitlementService entitlements,
            ITenantContext tenantContext,
            ILogger<FinancialMcpTools>? logger = null)
        {
            _financialService = financialService ?? throw new ArgumentNullException(nameof(financialService));
            _adminService = adminService ?? throw new ArgumentNullException(nameof(adminService));
            _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
            _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinancialMcpTools>.Instance;
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
        [Description("Retrieves the organization's Chart of Accounts (COA) including account codes, names, classifications (Asset, Liability, Equity, Revenue, Expense), normal balances, and current balances. Requires the workspace activeModules to include 'financials' or 'reports'. On plan restriction returns Status PlanRestricted instead of throwing.")]
        public Task<ChartOfAccountsMcpResponse> GetChartOfAccounts(
            [Description("Optional search text to filter accounts by code or name.")] string? filter = null,
            [Description("Optional classification filter (e.g., 'Asset', 'Liability', 'Equity', 'Revenue', 'Expense').")] string? accountClass = null,
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            if (!TryAuthorizeModules(out var denied))
            {
                return Task.FromResult(Denied<ChartOfAccountsMcpResponse>(denied));
            }

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
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
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
        [Description("Generates a Trial Balance report for the organization across a date range and accounting basis (Accrual or Cash), summarizing total debit and credit balances per account. Requires activeModules 'financials' or 'reports' and ReportsView permission. On restriction returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public Task<TrialBalanceMcpResponse> GetTrialBalance(
            [Description("Optional inclusive start date (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional inclusive end date (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            if (!TryAuthorizeModules(out var moduleDenied))
            {
                return Task.FromResult(Denied<TrialBalanceMcpResponse>(moduleDenied));
            }

            if (!TryAuthorizePermission(SystemPermissions.ReportsView, out var permissionDenied))
            {
                return Task.FromResult(Denied<TrialBalanceMcpResponse>(permissionDenied));
            }

            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate), inclusiveEndOfDay: true);
            ValidateDateRange(from, to);
            var basis = ParseBasis(accountingBasis);

            var rawItems = _financialService.TrialBalance(from, to, basis)?.ToList() ?? new List<TrialBalance>();

            var totalDebit = rawItems.Sum(i => i.Debit);
            var totalCredit = rawItems.Sum(i => i.Credit);

            var response = new TrialBalanceMcpResponse
            {
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
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
        [Description("Generates a cumulative Balance Sheet statement (Assets, Liabilities, and Equity) as of a given date and accounting basis (Accrual or Cash). Requires activeModules 'financials' or 'reports' and ReportsView permission. On restriction returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public Task<BalanceSheetMcpResponse> GetBalanceSheet(
            [Description("Optional cumulative as-of date (e.g., '2026-12-31'). Includes all activity up to and including this date.")] string? asOfDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            if (!TryAuthorizeModules(out var moduleDenied))
            {
                return Task.FromResult(Denied<BalanceSheetMcpResponse>(moduleDenied));
            }

            if (!TryAuthorizePermission(SystemPermissions.ReportsView, out var permissionDenied))
            {
                return Task.FromResult(Denied<BalanceSheetMcpResponse>(permissionDenied));
            }

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
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
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
        [Description("Generates an Income Statement (Profit & Loss) for the organization over a date range and accounting basis (Accrual or Cash), calculating total revenues, total expenses, and net income. Requires activeModules 'financials' or 'reports' and ReportsView permission. On restriction returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public Task<IncomeStatementMcpResponse> GetIncomeStatement(
            [Description("Optional period start date (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional period end date (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            if (!TryAuthorizeModules(out var moduleDenied))
            {
                return Task.FromResult(Denied<IncomeStatementMcpResponse>(moduleDenied));
            }

            if (!TryAuthorizePermission(SystemPermissions.ReportsView, out var permissionDenied))
            {
                return Task.FromResult(Denied<IncomeStatementMcpResponse>(permissionDenied));
            }

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
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
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
            Name = "get_cash_flow",
            Title = "Get Statement of Cash Flows",
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = false
        )]
        [Description("Generates a Statement of Cash Flows for the organization over a date range and accounting basis (Accrual or Cash) using the indirect method, calculating Operating, Investing, and Financing cash flows reconciling to cash balances. Requires activeModules 'financials' or 'reports' and ReportsView permission. On restriction returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public Task<CashFlowStatementMcpResponse> GetCashFlow(
            [Description("Optional period start date (e.g., '2026-01-01').")] string? fromDate = null,
            [Description("Optional period end date (e.g., '2026-12-31').")] string? toDate = null,
            [Description("Accounting basis: 'Accrual' (default) or 'Cash'.")] string? accountingBasis = "Accrual",
            [Description("Whether to include accounts with zero balances. Defaults to true.")] bool includeZeroBalances = true,
            CancellationToken cancellationToken = default)
        {
            EnsureTenantResolved();
            if (!TryAuthorizeModules(out var moduleDenied))
            {
                return Task.FromResult(Denied<CashFlowStatementMcpResponse>(moduleDenied));
            }

            if (!TryAuthorizePermission(SystemPermissions.ReportsView, out var permissionDenied))
            {
                return Task.FromResult(Denied<CashFlowStatementMcpResponse>(permissionDenied));
            }

            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate), inclusiveEndOfDay: true);
            ValidateDateRange(from, to);
            var basis = ParseBasis(accountingBasis);

            var cf = _financialService.CashFlow(from, to, basis);

            var operatingActivities = cf.OperatingActivities.ToList();
            var investingActivities = cf.InvestingActivities.ToList();
            var financingActivities = cf.FinancingActivities.ToList();

            if (!includeZeroBalances)
            {
                operatingActivities = operatingActivities.Where(a => a.Amount != 0m).ToList();
                investingActivities = investingActivities.Where(a => a.Amount != 0m).ToList();
                financingActivities = financingActivities.Where(a => a.Amount != 0m).ToList();
            }

            var isBalanced = basis == Core.Domain.AccountingBasis.Cash
                ? Math.Round(cf.StartingCash + cf.NetChangeInCash, 2) == Math.Round(cf.EndingCash, 2)
                : Math.Round(cf.OperatingCashFlow + cf.InvestingCashFlow + cf.FinancingCashFlow, 2) == Math.Round(cf.NetChangeInCash, 2);

            var response = new CashFlowStatementMcpResponse
            {
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
                Message = cf.Message,
                AccountingBasis = basis.ToString(),
                FromDate = from,
                ToDate = to,
                NetIncome = cf.NetIncome,
                OperatingCashFlow = cf.OperatingCashFlow,
                InvestingCashFlow = cf.InvestingCashFlow,
                FinancingCashFlow = cf.FinancingCashFlow,
                NetChangeInCash = cf.NetChangeInCash,
                StartingCash = cf.StartingCash,
                EndingCash = cf.EndingCash,
                IsBalanced = isBalanced,
                OperatingActivities = operatingActivities.Select(a => new CashFlowLineMcp
                {
                    AccountId = a.AccountId,
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    LineType = a.LineType,
                    Amount = a.Amount
                }).ToList(),
                InvestingActivities = investingActivities.Select(a => new CashFlowLineMcp
                {
                    AccountId = a.AccountId,
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    LineType = a.LineType,
                    Amount = a.Amount
                }).ToList(),
                FinancingActivities = financingActivities.Select(a => new CashFlowLineMcp
                {
                    AccountId = a.AccountId,
                    AccountCode = a.AccountCode,
                    AccountName = a.AccountName,
                    LineType = a.LineType,
                    Amount = a.Amount
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
        [Description("Retrieves a paged register of posted General Ledger journal transactions with complete lines (accounts, debits, credits, narrations, and document types). Requires activeModules 'financials' or 'reports' and FinancialsJournalView permission. On restriction returns Status PlanRestricted or AccessDenied instead of throwing.")]
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
            if (!TryAuthorizeModules(out var moduleDenied))
            {
                return Task.FromResult(Denied<GeneralLedgerTransactionsMcpResponse>(moduleDenied));
            }

            if (!TryAuthorizePermission(SystemPermissions.FinancialsJournalView, out var permissionDenied))
            {
                return Task.FromResult(Denied<GeneralLedgerTransactionsMcpResponse>(permissionDenied));
            }

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
                Organization = OrgLabel(),
                Status = FinancialMcpStatus.Success,
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

        private bool TryAuthorizeModules(out EntitlementDenial denial)
        {
            var companyId = _tenantContext.RequireCompanyId();
            var decision = _entitlements.Evaluate(
                companyId,
                new EntitlementAction.Module(ModuleAccess.Read, FinancialOrReportsModules));
            if (decision.Allowed)
            {
                denial = default;
                return true;
            }

            denial = BuildPlanRestrictedDenial(FinancialOrReportsModules);
            return false;
        }

        private bool TryAuthorizePermission(string requiredPermission, out EntitlementDenial denial)
        {
            if (_tenantContext.Role == CompanyMemberRole.Owner || _tenantContext.Role == CompanyMemberRole.Admin)
            {
                denial = default;
                return true;
            }

            if (_tenantContext.Permissions.Contains(requiredPermission))
            {
                denial = default;
                return true;
            }

            denial = new EntitlementDenial(
                FinancialMcpStatus.AccessDenied,
                $"Access denied. You do not have the required permission ({requiredPermission}) to access this resource.",
                RequiredModules: null,
                UpgradePlanHint: null);
            return false;
        }

        private EntitlementDenial BuildPlanRestrictedDenial(IReadOnlyList<string> requiredModules)
        {
            TenantSubscriptionDto? subscription = null;
            try
            {
                subscription = _adminService.GetCurrentSubscription();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not load subscription while building PlanRestricted message");
            }

            var planKey = Company.NormalizePlanKey(subscription?.Plan ?? "unknown");
            var planDisplay = PlanDisplayName(planKey);
            var org = OrgLabel();
            var modulesLabel = string.Join("' or '", requiredModules);

            string message;
            if (planKey.StartsWith("invoicing", StringComparison.Ordinal))
            {
                message =
                    $"The active workspace '{org}' is on the {planDisplay} plan, which includes quotes, invoicing, and expenses, but does not include double-entry accounting or financial statements. Upgrade to the Accounting or Enterprise plan under Settings > Subscription to access financial reports and statements.";
            }
            else
            {
                message =
                    $"The active workspace '{org}' does not have the '{modulesLabel}' module enabled (plan: {planDisplay}). Enable the required modules or upgrade under Settings > Subscription.";
            }

            return new EntitlementDenial(
                FinancialMcpStatus.PlanRestricted,
                message,
                RequiredModules: requiredModules,
                UpgradePlanHint: planKey.StartsWith("invoicing", StringComparison.Ordinal) || planKey == "unknown"
                    ? "accounting_free"
                    : null);
        }

        private T Denied<T>(EntitlementDenial denial) where T : FinancialMcpBaseResponse, new()
        {
            _logger.LogInformation(
                "Financial MCP entitlement {Status} for company {CompanyId}",
                denial.Status,
                _tenantContext.CompanyId);

            return new T
            {
                Organization = OrgLabel(),
                Status = denial.Status,
                Message = denial.Message,
                RequiredModules = denial.RequiredModules,
                UpgradePlanHint = denial.UpgradePlanHint
            };
        }

        private string OrgLabel() =>
            _tenantContext.CompanySlug ?? $"Tenant-{_tenantContext.CompanyId}";

        private static string PlanDisplayName(string planKey)
        {
            var key = Company.NormalizePlanKey(planKey);
            if (key.StartsWith("invoicing", StringComparison.Ordinal)) return "Invoicing";
            if (key.StartsWith("accounting", StringComparison.Ordinal)) return "Accounting";
            if (key is "enterprise") return "Enterprise";
            return string.IsNullOrWhiteSpace(planKey) ? "unknown" : planKey;
        }

        private readonly record struct EntitlementDenial(
            string Status,
            string Message,
            IReadOnlyList<string>? RequiredModules,
            string? UpgradePlanHint);

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

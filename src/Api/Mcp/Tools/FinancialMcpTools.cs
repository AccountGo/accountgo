#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Mcp.Models;
using Core.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Services.Financial;

namespace Api.Mcp.Tools
{
    [McpServerToolType]
    public class FinancialMcpTools
    {
        private readonly IFinancialService? _financialService;
        private readonly ITenantContext? _tenantContext;

        public FinancialMcpTools()
        {
        }

        public FinancialMcpTools(IFinancialService financialService, ITenantContext tenantContext)
        {
            _financialService = financialService ?? throw new ArgumentNullException(nameof(financialService));
            _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        }

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
            IServiceProvider? serviceProvider = null,
            CancellationToken cancellationToken = default)
        {
            var tenantContext = _tenantContext ?? serviceProvider?.GetRequiredService<ITenantContext>()
                ?? throw new InvalidOperationException("Tenant context is required.");
            var financialService = _financialService ?? serviceProvider?.GetRequiredService<IFinancialService>()
                ?? throw new InvalidOperationException("Financial service is required.");

            if (!tenantContext.IsResolved || tenantContext.CompanyId == null)
            {
                throw new InvalidOperationException("Tenant context is required. Ensure 'X-AccountGo-Org' header is supplied with an authorized organization slug.");
            }

            var allAccounts = financialService.GetAccounts()?.ToList() ?? new List<Core.Domain.Financials.Account>();

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
            foreach (var account in allAccounts)
            {
                var className = account.AccountClass?.Name ?? "Unclassified";
                classificationCounts[className] = classificationCounts.GetValueOrDefault(className, 0) + 1;
            }

            var response = new ChartOfAccountsMcpResponse
            {
                Organization = tenantContext.CompanySlug ?? $"Tenant-{tenantContext.CompanyId}",
                TotalAccounts = filteredAccounts.Count,
                AsOfDateUtc = DateTime.UtcNow,
                ClassificationCounts = classificationCounts,
                Accounts = filteredAccounts.Select(a => new AccountMcpSummary
                {
                    Id = a.Id,
                    Code = a.AccountCode ?? string.Empty,
                    Name = a.AccountName ?? string.Empty,
                    Description = a.Description,
                    AccountClass = a.AccountClass?.Name ?? "Unknown",
                    NormalBalance = a.AccountClass?.NormalBalance ?? (a.DrOrCrSide == Core.Domain.DrOrCrSide.Cr ? "Cr" : "Dr"),
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
    }
}

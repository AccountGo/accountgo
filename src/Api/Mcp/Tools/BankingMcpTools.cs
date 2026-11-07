#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Mcp.Models;
using Core.Tenancy;
using ModelContextProtocol.Server;

namespace Api.Mcp.Tools
{
    /// <summary>Bank accounts, registers, reconciliations and imported statement lines, as the Banking pages show them.</summary>
    [McpServerToolType]
    public sealed class BankingMcpTools : WorkspaceMcpToolBase
    {
        public BankingMcpTools(McpControllerReader reader, ITenantContext tenant) : base(reader, tenant)
        {
        }

        [McpServerTool(Name = "list_bank_accounts", Title = "List Bank Accounts", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the workspace's bank and cash accounts with institution, masked account number, currency, linked ledger account, current balance and defaults. Full account and routing numbers are never returned. With bankId, returns that one account. Requires the Sales, Purchases or Financials module and permission to view bank accounts.")]
        public Task<WorkspaceDataMcpResponse> ListBankAccounts(
            [Description("Optional bank account id for one account.")] int? bankId = null,
            [Description("Include archived accounts in the list. Defaults to false.")] bool includeArchived = false,
            CancellationToken cancellationToken = default)
        {
            if (bankId is int id)
            {
                return RecordAsync<BankingController>("bankAccount", nameof(BankingController.GetAccount), c => c.GetAccount(id),
                    link: PagePath($"banking/{id}"), arguments: new Dictionary<string, object?> { ["id"] = id });
            }

            return RecordAsync<BankingController>("bankAccounts", nameof(BankingController.ListAccounts), c => c.ListAccounts(includeArchived),
                link: PagePath("banking"), arguments: new Dictionary<string, object?> { ["includeArchived"] = includeArchived });
        }

        [McpServerTool(Name = "get_bank_register", Title = "Get Bank Register", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one bank account's register: dated money in and out with payee, memo, source document, running balance and cleared/reconciled state, paged. Requires permission to view bank accounts.")]
        public Task<WorkspaceDataMcpResponse> GetBankRegister(
            [Description("The bank account id (from list_bank_accounts).")] int bankId,
            [Description("Optional earliest date (e.g. '2026-01-01').")] string? fromDate = null,
            [Description("Optional latest date (e.g. '2026-12-31').")] string? toDate = null,
            [Description("Page number, starting at 1. Defaults to 1.")] int page = 1,
            [Description("Rows per page (1-200). Defaults to 50.")] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            ParseDate(fromDate, nameof(fromDate));
            ParseDate(toDate, nameof(toDate));
            if (page < 1) throw new ArgumentException("Page must be 1 or more.", nameof(page));
            if (pageSize < 1 || pageSize > 200) throw new ArgumentException("pageSize must be between 1 and 200.", nameof(pageSize));

            return RecordAsync<BankingController>("bankRegister", nameof(BankingController.GetRegister),
                c => c.GetRegister(bankId, fromDate, toDate, page, pageSize),
                link: PagePath($"banking/{bankId}"),
                arguments: new Dictionary<string, object?>
                {
                    ["id"] = bankId, ["from"] = fromDate, ["to"] = toDate, ["page"] = page, ["pageSize"] = pageSize
                });
        }

        [McpServerTool(Name = "get_bank_reconciliations", Title = "Get Bank Reconciliations", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Without reconciliationId: lists a bank account's reconciliations (statement date, statement balance, status, difference). With reconciliationId: that reconciliation in full, including cleared and outstanding items. Also answers whether bank accounts are reconciled up to a date when bankId is omitted and asOfDate is given (close status). Requires the Financials module and permission to view bank accounts.")]
        public Task<WorkspaceDataMcpResponse> GetBankReconciliations(
            [Description("The bank account id. Required for the list.")] int? bankId = null,
            [Description("Optional reconciliation id for the full detail.")] int? reconciliationId = null,
            [Description("With no bankId or reconciliationId: the date to check reconciliation close status for (e.g. '2026-10-31').")] string? asOfDate = null,
            CancellationToken cancellationToken = default)
        {
            if (reconciliationId is int reconId)
            {
                return RecordAsync<BankingController>("bankReconciliation", nameof(BankingController.GetReconciliation), c => c.GetReconciliation(reconId),
                    link: bankId is int b ? PagePath($"banking/{b}/reconcile/{reconId}") : PagePath("banking/reconcile"),
                    arguments: new Dictionary<string, object?> { ["id"] = reconId });
            }

            if (bankId is int id)
            {
                return RecordAsync<BankingController>("bankReconciliations", nameof(BankingController.ListReconciliations), c => c.ListReconciliations(id),
                    link: PagePath($"banking/{id}/reconcile"), arguments: new Dictionary<string, object?> { ["bankId"] = id });
            }

            ParseDate(asOfDate, nameof(asOfDate));
            return RecordAsync<BankingController>("bankCloseStatus", nameof(BankingController.GetCloseStatus), c => c.GetCloseStatus(asOfDate),
                link: PagePath("banking/reconcile"), arguments: new Dictionary<string, object?> { ["asOf"] = asOfDate });
        }

        [McpServerTool(Name = "get_bank_statement_lines", Title = "Get Bank Statement Lines", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists imported bank statement lines for one bank account with date, description, amount and match state, paged. Filter by status: unmatched (still to review), matched, excluded or all. Requires statement import for the workspace and permission to view bank accounts.")]
        public Task<WorkspaceDataMcpResponse> GetBankStatementLines(
            [Description("The bank account id (from list_bank_accounts).")] int bankId,
            [Description("Status filter: unmatched, matched, excluded or all. Defaults to unmatched.")] string? status = "unmatched",
            [Description("Page number, starting at 1. Defaults to 1.")] int page = 1,
            [Description("Rows per page (1-200). Defaults to 50.")] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            if (page < 1) throw new ArgumentException("Page must be 1 or more.", nameof(page));
            if (pageSize < 1 || pageSize > 200) throw new ArgumentException("pageSize must be between 1 and 200.", nameof(pageSize));

            return RecordAsync<BankingController>("bankStatementLines", nameof(BankingController.ListStatementLines),
                c => c.ListStatementLines(bankId, status, page, pageSize, null),
                link: PagePath("banking/needs-review"),
                arguments: new Dictionary<string, object?>
                {
                    ["bankId"] = bankId, ["status"] = status, ["page"] = page, ["pageSize"] = pageSize, ["lineId"] = null
                });
        }
    }
}

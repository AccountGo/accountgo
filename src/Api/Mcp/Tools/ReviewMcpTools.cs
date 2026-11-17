#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Mcp.Models;
using Core.Tenancy;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Api.Mcp.Tools
{
    /// <summary>Read-only books-review evidence through the existing pages' GET actions and access rules.</summary>
    /// <param name="reader">Executes the selected GET action behind its filters.</param>
    /// <param name="tenant">The resolved workspace member.</param>
    [McpServerToolType]
    public sealed class ReviewMcpTools(McpControllerReader reader, ITenantContext tenant) : WorkspaceMcpToolBase(reader, tenant)
    {
        /// <summary>Reads fiscal-year boundaries and close status without creating a fiscal year.</summary>
        [McpServerTool(Name = "get_fiscal_years", Title = "Get Fiscal Years", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the workspace's fiscal years: code, name, start and end dates (yyyy-MM-dd), active flag, status (Open, Ended or Closed), closedAt (UTC timestamp with an explicit offset, or null), closedBy, closingGeneralLedgerHeaderId and isPersisted. Closed means the year was closed; the active flag does not mean closed or open. Includes the page's unsaved current-year fallback (isPersisted false) and does not create a fiscal year or invent a closing journal. A row does not include close readiness, a close package, the books lock, correction eligibility or whether the books are correct. Use get_workspace_context for the books lock. Available to authenticated workspace members.")]
        public Task<WorkspaceDataMcpResponse> GetFiscalYears(CancellationToken cancellationToken = default) =>
            RecordAsync<AdministrationController>("fiscalYears", nameof(AdministrationController.FinancialYears), c => c.FinancialYears(),
                link: PagePath("settings"));

        /// <summary>Reads workspace membership and the functional role definitions shown on the Members page.</summary>
        [McpServerTool(Name = "get_members_and_roles", Title = "Get Members and Roles", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns current workspace members, governance roles, membership status, assigned security roles, and role definitions with permissions and workspace member counts. Available to authenticated workspace members under the same rules as the Members GET endpoints. This is current access, not historical period-end assignments or proof of operating control effectiveness. Use get_workspace_context for the caller's effective permissions.")]
        public async Task<WorkspaceDataMcpResponse> GetMembersAndRoles(CancellationToken cancellationToken = default)
        {
            var members = await Reader.ReadAsync<WorkspaceMembersController>(nameof(WorkspaceMembersController.Members), c => c.Members());
            if (!members.Succeeded) return Refused("membersAndRoles", members);
            var roles = await Reader.ReadAsync<WorkspaceMembersController>(nameof(WorkspaceMembersController.SecurityRoles), c => c.SecurityRoles());
            if (!roles.Succeeded) return Refused("membersAndRoles", roles);
            return Success("membersAndRoles", new { Members = members.Value, SecurityRoles = roles.Value }, PagePath("members"));
        }

        /// <summary>Reads the existing statement of changes in equity and optional comparison period.</summary>
        [McpServerTool(Name = "get_equity_statement", Title = "Get Equity Statement", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns the statement of changes in equity: opening balances, profit, contributions, distributions, adjustments, closing balances and balance-sheet checks, including partner columns where applicable. Optional comparative period. Uses the endpoint's date defaults and basis. Requires Financials or Reports and reports.view; plan and permission refusals return structured statuses. Use get_balance_sheet for closing balances.")]
        public Task<WorkspaceDataMcpResponse> GetEquityStatement(
            [Description("Optional inclusive start date, yyyy-MM-dd.")] string? from = null,
            [Description("Optional inclusive end date, yyyy-MM-dd.")] string? to = null,
            [Description("Accrual or Cash. Defaults to Accrual.")] string basis = "Accrual",
            [Description("Optional comparison start date, yyyy-MM-dd; requires compareTo.")] string? compareFrom = null,
            [Description("Optional comparison end date, yyyy-MM-dd; requires compareFrom.")] string? compareTo = null,
            CancellationToken cancellationToken = default)
        {
            const string kind = "equityStatement";
            if (!TryDateRange(from, to, out var start, out var end)
                || !TryDateRange(compareFrom, compareTo, out var comparisonStart, out var comparisonEnd)
                || comparisonStart.HasValue != comparisonEnd.HasValue)
                return Task.FromResult(Invalid(kind, "Provide valid yyyy-MM-dd date ranges and both comparison dates."));
            if (!Enum.TryParse<Core.Domain.AccountingBasis>(basis, true, out var accountingBasis)
                || !Enum.IsDefined(accountingBasis))
                return Task.FromResult(Invalid(kind, "Provide a valid accounting basis: Accrual or Cash."));
            return RecordAsync<FinancialsController>(kind, nameof(FinancialsController.EquityStatement),
                c => c.EquityStatement(start, end, accountingBasis, comparisonStart, comparisonEnd), PagePath("reports"),
                new Dictionary<string, object?> { ["from"] = start, ["to"] = end, ["basis"] = accountingBasis,
                    ["compareFrom"] = comparisonStart, ["compareTo"] = comparisonEnd });
        }

        /// <summary>Reads the tax context, or one period's report when both dates are supplied.</summary>
        [McpServerTool(Name = "get_tax_report", Title = "Get Tax Report", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Without from/to, returns the Tax page's settings, recent periods and filing register together as settings, periods and filings. With both dates, returns that period's report: output/input/net tax, documents, documentsTruncated, warnings, ledger check, lock, period filings and changed-since-filing. Requires reports.view and Sales, Purchases, Financials or Reports. Default reports may be truncated; includeAllDocuments explicitly requests all source documents. Use get_tax_rates for tax-code setup and get_fiscal_years for fiscal boundaries. Does not record or amend filings.")]
        public async Task<WorkspaceDataMcpResponse> GetTaxReport(
            [Description("Optional report start date, yyyy-MM-dd. Supply both from and to, or neither for the combined tax context.")] string? from = null,
            [Description("Optional report end date, yyyy-MM-dd. Requires from.")] string? to = null,
            [Description("For a period report, explicitly request all source documents instead of the endpoint's bounded default.")] bool includeAllDocuments = false,
            [Description("For the default context, number of recent periods (1-24). Defaults to 8.")] int count = 8,
            CancellationToken cancellationToken = default)
        {
            const string kind = "taxReport";
            if (count < 1 || count > 24)
                return Invalid(kind, "Provide count between 1 and 24.");
            if (!TryDateRange(from, to, out var start, out var end) || start.HasValue != end.HasValue)
                return Invalid(kind, "Provide both from and to in an ordered yyyy-MM-dd date range, or neither.");
            if (start.HasValue)
                return await RecordAsync<TaxReportsController>(kind, nameof(TaxReportsController.GetReport),
                    c => c.GetReport(from, to, includeAllDocuments), PagePath("tax"),
                    new Dictionary<string, object?> { ["from"] = from, ["to"] = to, ["includeAllDocuments"] = includeAllDocuments });

            var settings = await Reader.ReadAsync<TaxReportsController>(nameof(TaxReportsController.GetSettings), c => c.GetSettings());
            if (!settings.Succeeded) return Refused(kind, settings);
            var periods = await Reader.ReadAsync<TaxReportsController>(nameof(TaxReportsController.GetPeriods), c => c.GetPeriods(count),
                new Dictionary<string, object?> { ["count"] = count });
            if (!periods.Succeeded) return Refused(kind, periods);
            var filings = await Reader.ReadAsync<TaxReportsController>(nameof(TaxReportsController.ListFilings), c => c.ListFilings());
            if (!filings.Succeeded) return Refused(kind, filings);
            return Success(kind, new { Settings = settings.Value, Periods = periods.Value, Filings = filings.Value }, PagePath("tax"));
        }

        /// <summary>Searches receipt summaries using the Sales Receipts GET and its access rules.</summary>
        [McpServerTool(Name = "search_receipts", Title = "Search Receipts", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("With receiptId, returns the receipt detail from the Sales page. Otherwise lists sales receipt summaries newest first: receipt number, client, date, amount, unallocated amount, payment method, reference and memo. Filter by text, client and inclusive receipt dates, then page. Requires Sales and sales.receipts.view. The list endpoint does not expose void/posting status or allocation detail. Use get_invoice for invoice payment evidence. Links open Sales; there is no standalone receipt detail page.")]
        public async Task<WorkspaceDataMcpResponse> SearchReceipts(
            [Description("Optional positive receipt id. Returns the receipt detail instead of a list; list filters do not apply.")] int? receiptId = null,
            [Description("Optional text in receipt number, client name, reference or memo.")] string? query = null,
            [Description("Optional positive client id.")] int? clientId = null,
            [Description("Optional earliest receipt date, yyyy-MM-dd.")] string? fromDate = null,
            [Description("Optional latest receipt date, yyyy-MM-dd.")] string? toDate = null,
            [Description("Records to skip, at least zero.")] int skip = 0,
            [Description("Records to return, 1-100. Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            const string kind = "receipts";
            if (receiptId.HasValue)
            {
                if (receiptId <= 0) return Invalid(kind, "Provide a positive receipt id.");
                return await RecordAsync<SalesController>("receipt", nameof(SalesController.SalesReceipt),
                    c => c.SalesReceipt(receiptId.Value), PagePath("sales"),
                    new Dictionary<string, object?> { ["id"] = receiptId.Value });
            }
            if (skip < 0 || take < 1 || take > MaxTake || clientId <= 0)
                return Invalid(kind, "Provide a positive client id, skip >= 0 and take between 1 and 100.");
            if (!TryDateRange(fromDate, toDate, out var from, out var to))
                return Invalid(kind, "Provide an ordered yyyy-MM-dd receipt date range.");
            var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.SalesReceipts), c => c.SalesReceipts());
            if (!read.Succeeded) return Refused(kind, read);
            var receipts = ((IEnumerable<Dto.Sales.SalesReceipt>?)read.Value ?? Array.Empty<Dto.Sales.SalesReceipt>())
                .Where(r => !clientId.HasValue || r.CustomerId == clientId)
                .Where(r => !from.HasValue || r.ReceiptDate.Date >= from)
                .Where(r => !to.HasValue || r.ReceiptDate.Date <= to)
                .Where(r => Matches(query, r.ReceiptNo, r.CustomerName, r.ReferenceNo, r.Memo))
                .OrderByDescending(r => r.ReceiptDate).ThenByDescending(r => r.Id)
                .Select(r => new { r.Id, r.ReceiptNo, r.CustomerId, r.CustomerName, r.ReceiptDate, r.Amount,
                    r.RemainingAmountToAllocate, r.PaymentMethod, r.ReferenceNo, r.Memo, Link = PagePath("sales") });
            return Page(kind, receipts, skip, take, PagePath("sales"));
        }

        /// <summary>Reads aggregate aging of currently uncleared bank entries through the selected date.</summary>
        [McpServerTool(Name = "get_bank_outstanding_items", Title = "Get Bank Outstanding Aging", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns bank outstanding aging bucket totals and counts, deposits in transit, outstanding payments and stale-item flags. Requires Financials and banking.accounts.view. Despite the tool name, the endpoint returns aggregate buckets, not individual items. asOfDate limits entry dates and determines age; clearance uses current reconciliation status, so this is not a historical reconstruction of outstanding items at cutoff. Use get_bank_register for individual entries.")]
        public Task<WorkspaceDataMcpResponse> GetBankOutstandingItems(
            [Description("Positive bank account id from list_bank_accounts.")] int bankId,
            [Description("Optional cutoff date, yyyy-MM-dd. Defaults to today UTC.")] string? asOfDate = null,
            CancellationToken cancellationToken = default)
        {
            if (bankId <= 0 || !TryDateRange(asOfDate, null, out _, out _))
                return Task.FromResult(Invalid("bankOutstandingAging", "Provide a positive bank id and an optional yyyy-MM-dd date."));
            return RecordAsync<BankingController>("bankOutstandingAging", nameof(BankingController.GetOutstandingAging),
                c => c.GetOutstandingAging(bankId, asOfDate), PagePath($"banking/{bankId}"),
                new Dictionary<string, object?> { ["id"] = bankId, ["asOf"] = asOfDate });
        }

        /// <summary>Reads change evidence without treating absent history as proof that no event occurred.</summary>
        [McpServerTool(Name = "get_record_history", Title = "Get Record History", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns recorded changes with actor, time, action and before/after fields. Requires audit.logs.view. Supply entityType and entityId together for single-record history, optionally including children. Omit both for the workspace activity feed: its plan may refuse access, limit it to 90 days, or allow full history; single-record history has no plan window. Preserves items, nextCursor and windowDays. Use nextCursor to continue. Full bank numbers are redacted. Absence of history is not proof that an event never occurred; older records may lack attribution. Use get_invoice, get_bill or get_journal_entry for source-document details.")]
        public Task<WorkspaceDataMcpResponse> GetRecordHistory(
            [Description("Exact recorded entity type, for example SalesInvoiceHeader; requires entityId.")] string? entityType = null,
            [Description("Positive record id; requires entityType.")] int? entityId = null,
            [Description("Include recorded child changes for the selected record.")] bool includeChildren = false,
            [Description("Optional actor user id from get_members_and_roles.")] string? actorUserId = null,
            [Description("Optional inclusive ISO-8601 timestamp, with UTC offset recommended.")] string? from = null,
            [Description("Optional inclusive ISO-8601 timestamp, with UTC offset recommended.")] string? to = null,
            [Description("Optional positive nextCursor returned by the previous page.")] long? cursor = null,
            [Description("Page size, 1-100. Defaults to 50.")] int limit = 50,
            CancellationToken cancellationToken = default)
        {
            const string kind = "recordHistory";
            if (string.IsNullOrWhiteSpace(entityType) == entityId.HasValue || entityId <= 0 || cursor <= 0
                || limit < 1 || limit > 100 || (includeChildren && !entityId.HasValue))
                return Task.FromResult(Invalid(kind, "Provide entityType and a positive entityId together, a positive cursor, and limit between 1 and 100. Children require a record."));
            if (!TryTimestamp(from, out var start) || !TryTimestamp(to, out var end)
                || (start.HasValue && end.HasValue && start > end))
                return Task.FromResult(Invalid(kind, "Provide an ordered ISO-8601 timestamp range."));
            return RecordAsync<AuditActivityController>(kind, nameof(AuditActivityController.Activity),
                c => c.Activity(entityType, entityId, includeChildren, actorUserId, start, end, cursor, limit),
                arguments: new Dictionary<string, object?> { ["entityType"] = entityType, ["entityId"] = entityId,
                    ["includeChildren"] = includeChildren, ["actorUserId"] = actorUserId,
                    ["from"] = start, ["to"] = end, ["cursor"] = cursor, ["limit"] = limit });
        }

        private static bool TryTimestamp(string? value, out DateTimeOffset? timestamp)
        {
            timestamp = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                return false;
            timestamp = parsed;
            return true;
        }

        /// <summary>Filters and pages already-computed movement rows without recalculating running quantities.</summary>
        [McpServerTool(Name = "get_stock_movements", Title = "Get Stock Movements", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns stock movement rows newest first with item, date, source document, incoming/outgoing quantity, unit and total cost, memo and the page's running quantity. Requires Inventory and inventory.items.view. Filter by one item for meaningful per-item running quantities; the existing all-items endpoint accumulates across its returned items. Date filters and paging preserve the original running quantities, not a recomputation for the selected window. Does not provide physical-count evidence. Use search_items for item IDs and get_invoice or get_bill for source documents.")]
        public async Task<WorkspaceDataMcpResponse> GetStockMovements(
            [Description("Optional positive stock item id.")] int? itemId = null,
            [Description("Optional earliest movement date, yyyy-MM-dd.")] string? fromDate = null,
            [Description("Optional latest movement date, yyyy-MM-dd.")] string? toDate = null,
            [Description("Records to skip, at least zero.")] int skip = 0,
            [Description("Records to return, 1-100. Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            const string kind = "stockMovements";
            if (itemId <= 0 || skip < 0 || take < 1 || take > MaxTake)
                return Invalid(kind, "Provide a positive item id, skip >= 0 and take between 1 and 100.");
            if (!TryDateRange(fromDate, toDate, out var from, out var to))
                return Invalid(kind, "Provide an ordered yyyy-MM-dd movement date range.");
            McpReadResult read;
            try
            {
                read = await Reader.ReadAsync<StockController>(nameof(StockController.Movements), c => c.Movements(itemId),
                    new Dictionary<string, object?> { ["itemId"] = itemId });
            }
            catch (TenantEntityNotFoundException)
            {
                return NotFound(kind, "Stock item not found in this workspace.");
            }
            if (!read.Succeeded) return Refused(kind, read);
            // Keep row fields and decimal values intact through the Newtonsoft response serializer.
            var rows = JArray.FromObject(read.Value!)
                .Where(r => !from.HasValue || r.Value<DateTime>("date").Date >= from)
                .Where(r => !to.HasValue || r.Value<DateTime>("date").Date <= to);
            return Page(kind, rows, skip, take, PagePath(itemId.HasValue ? $"items/inventory/{itemId}" : "items/inventory"));
        }

        /// <summary>Validates optional ISO dates and their inclusive range without throwing a tool error.</summary>
        private static bool TryDateRange(string? from, string? to, out DateTime? start, out DateTime? end)
        {
            start = end = null;
            if (!string.IsNullOrWhiteSpace(from))
            {
                if (!DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    return false;
                start = parsed;
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                if (!DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    return false;
                end = parsed;
            }
            return !start.HasValue || !end.HasValue || start <= end;
        }

    }
}

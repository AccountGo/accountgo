#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Controllers;
using Api.Mcp.Models;
using Core.Tenancy;
using ModelContextProtocol.Server;
using Services.Financial;

namespace Api.Mcp.Tools
{
    /// <summary>Workspace context, dashboard, journals, catalog, tax and linked entries.</summary>
    [McpServerToolType]
    public sealed class WorkspaceMcpTools : WorkspaceMcpToolBase
    {
        private readonly IFinancialService _financialService;

        public WorkspaceMcpTools(McpControllerReader reader, ITenantContext tenant, IFinancialService financialService)
            : base(reader, tenant)
        {
            _financialService = financialService;
        }

        [McpServerTool(Name = "get_workspace_context", Title = "Get Workspace Context", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns the current workspace and what the signed-in member can do in it: name, plan, active modules, subscription status, the member's role and effective permission keys, base currency, entity type, fiscal year end, operating settings (purpose, collaboration state, access profile, journal workflow, allowed next actions) and the books lock date. Call this first when an answer depends on plan, permissions, currency or setup.")]
        public async Task<WorkspaceDataMcpResponse> GetWorkspaceContext(CancellationToken cancellationToken = default)
        {
            var current = await Reader.ReadAsync<OrganizationsController>(nameof(OrganizationsController.GetCurrent), c => c.GetCurrent());
            if (!current.Succeeded) return Refused("workspaceContext", current);

            var booksLock = await Reader.ReadAsync<OrganizationsController>(nameof(OrganizationsController.GetBooksLock), c => c.GetBooksLock());
            return Success("workspaceContext", new { Workspace = current.Value, BooksLock = booksLock.Succeeded ? booksLock.Value : null },
                link: PagePath("settings"));
        }

        [McpServerTool(Name = "get_dashboard_summary", Title = "Get Dashboard Summary", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns the dashboard figures for a period: cash position, income and expenses, receivables and payables totals with the largest open items, and recent activity. Defaults to the dashboard's own period when dates are omitted. Requires permission to view the dashboard.")]
        public Task<WorkspaceDataMcpResponse> GetDashboardSummary(
            [Description("Optional period start (e.g. '2026-01-01').")] string? fromDate = null,
            [Description("Optional period end (e.g. '2026-12-31').")] string? toDate = null,
            [Description("How many top receivables/payables and recent activities to include (1-20). Defaults to 5.")] int top = 5,
            CancellationToken cancellationToken = default)
        {
            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate));
            if (top < 1 || top > 20) throw new ArgumentException("top must be between 1 and 20.", nameof(top));

            return RecordAsync<DashboardController>("dashboardSummary", nameof(DashboardController.Summary),
                c => c.Summary(from, to, top, top, top), link: PagePath("dashboard"),
                arguments: new Dictionary<string, object?>
                {
                    ["from"] = from, ["to"] = to, ["topReceivables"] = top, ["topPayables"] = top, ["recentActivity"] = top
                });
        }

        [McpServerTool(Name = "search_journal_entries", Title = "Search Journal Entries", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists manual journal entries newest first, including drafts that are not posted yet (the posted ledger itself is get_general_ledger_transactions). Filter by posted state, date range or text in reference/memo. Each row has date, reference, memo, posted flag, debit and credit totals and line count. Requires the Financials module and permission to view journals.")]
        public async Task<WorkspaceDataMcpResponse> SearchJournalEntries(
            [Description("Optional text matched against reference number and memo.")] string? query = null,
            [Description("Optional posted filter: true for posted only, false for drafts only.")] bool? posted = null,
            [Description("Optional earliest journal date (e.g. '2026-01-01').")] string? fromDate = null,
            [Description("Optional latest journal date (e.g. '2026-12-31').")] string? toDate = null,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate));

            var read = await Reader.ReadAsync<FinancialsController>(nameof(FinancialsController.JournalEntries), c => c.JournalEntries());
            if (!read.Succeeded) return Refused("journalEntries", read);

            var entries = ((IEnumerable<Dto.Financial.JournalEntry>?)read.Value ?? Array.Empty<Dto.Financial.JournalEntry>())
                .Where(j => posted == null || (j.Posted == true) == posted)
                .Where(j => from == null || j.JournalDate.Date >= from)
                .Where(j => to == null || j.JournalDate.Date <= to)
                .Where(j => Matches(query, j.ReferenceNo, j.Memo))
                .OrderByDescending(j => j.JournalDate).ThenByDescending(j => j.Id)
                .Select(j => new
                {
                    j.Id,
                    Date = j.JournalDate,
                    j.ReferenceNo,
                    j.Memo,
                    Posted = j.Posted == true,
                    TotalDebit = j.debitAmount,
                    TotalCredit = j.creditAmount,
                    LineCount = j.JournalEntryLines.Count,
                    j.AdjustsFiscalYearCode
                })
                .ToList();
            return Page("journalEntries", entries, skip, take, PagePath("financials/journals"));
        }

        [McpServerTool(Name = "get_journal_entry", Title = "Get Journal Entry", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one journal entry with its lines (account code and name, debit or credit, amount, memo), totals, reference, memo and posted state. Use the id from search_journal_entries or a GL transaction's sourceDocumentId for document type JournalEntry. Requires the Financials module and permission to view journals.")]
        public Task<WorkspaceDataMcpResponse> GetJournalEntry(
            [Description("The journal entry id.")] int journalEntryId,
            CancellationToken cancellationToken = default) =>
            RecordAsync<FinancialsController>("journalEntry", nameof(FinancialsController.JournalEntry), c => c.JournalEntry(journalEntryId),
                link: PagePath("financials/journals"),
                arguments: new Dictionary<string, object?> { ["id"] = journalEntryId },
                shape: value => value is Dto.Financial.JournalEntry entry ? DescribeJournal(entry) : value);

        [McpServerTool(Name = "search_items", Title = "Search Products and Services", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the products and services catalog with code, name, price, cost, revenue account, whether stock is tracked and quantity on hand. Filter by text or include archived items; with itemId returns that one item. Requires the Sales or Purchases module and permission to view items.")]
        public async Task<WorkspaceDataMcpResponse> SearchItems(
            [Description("Optional item id for one item.")] int? itemId = null,
            [Description("Optional text matched against item code and name.")] string? query = null,
            [Description("Include archived items. Defaults to false.")] bool includeArchived = false,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            if (itemId is int id)
            {
                return await RecordAsync<CatalogController>("item", nameof(CatalogController.Item), c => c.Item(id),
                    link: PagePath("items"), arguments: new Dictionary<string, object?> { ["id"] = id });
            }

            ValidatePaging(skip, take);
            var read = await Reader.ReadAsync<CatalogController>(nameof(CatalogController.Items), c => c.Items());
            if (!read.Succeeded) return Refused("items", read);

            var items = ((IEnumerable<Dto.Inventory.CatalogItem>?)read.Value ?? Array.Empty<Dto.Inventory.CatalogItem>())
                .Where(i => includeArchived || !i.Archived)
                .Where(i => Matches(query, i.Code, i.Name))
                .ToList();
            return Page("items", items, skip, take, PagePath("items"));
        }

        [McpServerTool(Name = "get_tax_rates", Title = "Get Tax Rates", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns the workspace's tax setup: tax rates (code, name, rate, sales and purchase tax accounts) and tax groups. Requires permission to view tax settings.")]
        public Task<WorkspaceDataMcpResponse> GetTaxRates(CancellationToken cancellationToken = default) =>
            RecordAsync<TaxController>("taxRates", nameof(TaxController.Taxes), c => c.Taxes(), link: PagePath("settings"));

        [McpServerTool(Name = "get_linked_entries", Title = "Get Linked Entries", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("For a freelance or personal books workspace linked to the owner's other workspace: lists the linked entry pairs (money moved between work and personal books) with their state, and names the other workspace. Only the workspace Owner can see these.")]
        public Task<WorkspaceDataMcpResponse> GetLinkedEntries(CancellationToken cancellationToken = default) =>
            RecordAsync<LinkedEntriesController>("linkedEntries", nameof(LinkedEntriesController.List), c => c.List(null),
                arguments: new Dictionary<string, object?> { ["companyId"] = null });

        private object DescribeJournal(Dto.Financial.JournalEntry entry)
        {
            var accountIds = entry.JournalEntryLines.Where(l => l.AccountId.HasValue).Select(l => l.AccountId!.Value).ToHashSet();
            var accounts = (_financialService.GetAccounts() ?? Enumerable.Empty<Core.Domain.Financials.Account>())
                .Where(a => accountIds.Contains(a.Id))
                .ToDictionary(a => a.Id);

            return new
            {
                entry.Id,
                Date = entry.JournalDate,
                entry.ReferenceNo,
                entry.Memo,
                Posted = entry.Posted == true,
                entry.AdjustsFiscalYearCode,
                TotalDebit = entry.debitAmount,
                TotalCredit = entry.creditAmount,
                Lines = entry.JournalEntryLines.Select(l =>
                {
                    accounts.TryGetValue(l.AccountId ?? 0, out var account);
                    return new
                    {
                        l.Id,
                        l.AccountId,
                        AccountCode = account?.AccountCode,
                        AccountName = account?.AccountName,
                        Side = l.DrCr == 1 ? "Dr" : l.DrCr == 2 ? "Cr" : null,
                        Debit = l.DrCr == 1 ? l.Amount : null,
                        Credit = l.DrCr == 2 ? l.Amount : null,
                        l.Memo
                    };
                }).ToList()
            };
        }
    }
}

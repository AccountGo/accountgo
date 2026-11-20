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

namespace Api.Mcp.Tools
{
    /// <summary>Clients, invoices, receivables aging and recurring invoices, as the Sales pages show them.</summary>
    [McpServerToolType]
    public sealed class SalesMcpTools : WorkspaceMcpToolBase
    {
        public SalesMcpTools(McpControllerReader reader, ITenantContext tenant) : base(reader, tenant)
        {
        }

        [McpServerTool(Name = "search_clients", Title = "Search Clients", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the workspace's clients (customers) with what each owes: outstanding, overdue, paid to date and last invoice date. Filter by name/email/phone text, include archived clients, or only clients with an outstanding balance. Use get_client for one client's contacts and recent invoices. Requires the Sales module and permission to view clients; returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public async Task<WorkspaceDataMcpResponse> SearchClients(
            [Description("Optional text matched against client name, email and phone.")] string? query = null,
            [Description("Only clients that currently owe money. Defaults to false.")] bool withBalanceOnly = false,
            [Description("Include archived clients. Defaults to false.")] bool includeArchived = false,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.Clients), c => c.Clients());
            if (!read.Succeeded) return Refused("clients", read);

            var matching = ((IEnumerable<Dto.Sales.Client>?)read.Value ?? Array.Empty<Dto.Sales.Client>())
                .Where(c => includeArchived || !c.Archived)
                .Where(c => !withBalanceOnly || c.Outstanding > 0)
                .Where(c => Matches(query, c.Name, c.Email, c.Phone))
                .ToList();
            return await PageAsync("clients", matching, skip, take,
                c => new[] { ("cus", c.Id) },
                c => new
                {
                    Id = Pub("cus", c.Id),
                    c.Name,
                    c.Email,
                    c.Phone,
                    c.Country,
                    c.Archived,
                    c.Outstanding,
                    c.Overdue,
                    c.PaidToDate,
                    c.LastInvoiceDate,
                    Link = PagePath($"sales/clients/{Pub("cus", c.Id)}")
                },
                PagePath("sales/clients"), cancellationToken);
        }

        [McpServerTool(Name = "get_client", Title = "Get Client", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one client's details (address, contacts, payment term, balances) plus their most recent invoices with status and balance due. Requires the Sales module and permission to view clients; invoices are included only when the member may view invoices.")]
        public async Task<WorkspaceDataMcpResponse> GetClient(
            [Description("The client id, like 'cus_…' (from search_clients, an invoice's customerId, or a GL line).")] string clientId,
            [Description("How many recent invoices to include (0-50). Defaults to 10.")] int recentInvoices = 10,
            CancellationToken cancellationToken = default)
        {
            if (recentInvoices < 0 || recentInvoices > 50)
                throw new ArgumentException("recentInvoices must be between 0 and 50.", nameof(recentInvoices));

            var resolved = await ResolveIdAsync("client", clientId, "cus", nameof(clientId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            if (resolved.Id is not int clientKey) return Invalid("client", "Provide clientId.");

            var client = await Reader.ReadAsync<SalesController>(nameof(SalesController.Client), c => c.Client(clientKey),
                new Dictionary<string, object?> { ["id"] = clientKey });
            if (!client.Succeeded) return Refused("client", client);

            object? invoices = null;
            string? invoicesNote = null;
            if (recentInvoices > 0)
            {
                var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.SalesInvoices), c => c.SalesInvoices(clientKey),
                    new Dictionary<string, object?> { ["customerId"] = clientKey });
                if (read.Succeeded)
                {
                    var all = ((IEnumerable<Dto.Sales.SalesInvoice>?)read.Value ?? Array.Empty<Dto.Sales.SalesInvoice>()).ToList();
                    var recent = all.Take(recentInvoices).ToList();
                    await PrefetchAsync(InvoiceIds(recent), cancellationToken);
                    invoices = new
                    {
                        TotalCount = all.Count,
                        Items = recent.Select(InvoiceSummary).ToList()
                    };
                }
                else
                {
                    invoicesNote = read.Message;
                }
            }

            return Success("client", new { Client = client.Value, RecentInvoices = invoices, RecentInvoicesNote = invoicesNote },
                link: PagePath($"sales/clients/{Pub("cus", clientKey)}"));
        }

        [McpServerTool(Name = "search_invoices", Title = "Search Sales Invoices", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists sales invoices newest first with number, client, dates, status (Draft, Open, Overdue, Partially Paid, Paid, Void), total, paid amount, balance due and whether they are posted to the ledger. Filter by client, status, date range, overdue only, or text in number/client/reference. Use get_invoice for lines, tax, payments and emails. Requires the Sales module and permission to view invoices.")]
        public async Task<WorkspaceDataMcpResponse> SearchInvoices(
            [Description("Optional text matched against invoice number, client name and reference.")] string? query = null,
            [Description("Optional client id, like 'cus_…'.")] string? clientId = null,
            [Description("Optional status: Draft, Open, Overdue, Partially Paid, Paid or Void.")] string? status = null,
            [Description("Only invoices that are past due with a balance. Defaults to false.")] bool overdueOnly = false,
            [Description("Optional earliest invoice date (e.g. '2026-01-01').")] string? fromDate = null,
            [Description("Optional latest invoice date (e.g. '2026-12-31').")] string? toDate = null,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate));
            var resolved = await ResolveIdAsync("invoices", clientId, "cus", nameof(clientId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var clientKey = resolved.Id;

            var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.SalesInvoices), c => c.SalesInvoices(clientKey),
                new Dictionary<string, object?> { ["customerId"] = clientKey });
            if (!read.Succeeded) return Refused("invoices", read);

            var invoices = ((IEnumerable<Dto.Sales.SalesInvoice>?)read.Value ?? Array.Empty<Dto.Sales.SalesInvoice>())
                .Where(i => SameStatus(i.Status, status))
                .Where(i => !overdueOnly || SameStatus(i.Status, "Overdue"))
                .Where(i => from == null || i.InvoiceDate.Date >= from)
                .Where(i => to == null || i.InvoiceDate.Date <= to)
                .Where(i => Matches(query, i.No, i.CustomerName, i.ReferenceNo))
                .ToList();
            return await PageAsync("invoices", invoices, skip, take, i => InvoiceIds(new[] { i }), InvoiceSummary,
                PagePath("sales"), cancellationToken);
        }

        [McpServerTool(Name = "get_invoice", Title = "Get Sales Invoice", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one sales invoice in full: client and bill-to, dates and due date, status, lines (item, quantity, price, discount, tax, revenue account), totals, payments, balance due, notes, emails sent and posting state. Look it up by id like 'inv_…' (from search_invoices or a GL transaction's sourceDocumentId for document type SalesInvoice) or by invoice number. Requires the Sales module and permission to view invoices.")]
        public async Task<WorkspaceDataMcpResponse> GetInvoice(
            [Description("The invoice id, like 'inv_…'. Provide this or invoiceNo.")] string? invoiceId = null,
            [Description("The invoice number shown to the client, e.g. 'INV-0012'. Provide this or invoiceId.")] string? invoiceNo = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(invoiceId) && string.IsNullOrWhiteSpace(invoiceNo))
                return Invalid("invoice", "Provide invoiceId or invoiceNo.");

            var resolved = await ResolveIdAsync("invoice", invoiceId, "inv", nameof(invoiceId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var id = resolved.Id;
            if (id == null)
            {
                var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.SalesInvoices), c => c.SalesInvoices(null),
                    new Dictionary<string, object?> { ["customerId"] = null });
                if (!read.Succeeded) return Refused("invoice", read);
                id = ((IEnumerable<Dto.Sales.SalesInvoice>?)read.Value ?? Array.Empty<Dto.Sales.SalesInvoice>())
                    .FirstOrDefault(i => string.Equals(i.No?.Trim(), invoiceNo!.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
                if (id == null)
                    return NotFound("invoice", $"No invoice numbered '{invoiceNo}' in this workspace.");
            }

            return await RecordAsync<SalesController>("invoice", nameof(SalesController.SalesInvoice), c => c.SalesInvoice(id.Value),
                link: PagePath($"sales/{Pub("inv", id.Value)}"), arguments: new Dictionary<string, object?> { ["id"] = id });
        }

        [McpServerTool(Name = "get_receivables_aging", Title = "Get Receivables Aging", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Accounts receivable aging: totals in Current, 1-30, 31-60, 61-90 and over 90 days past due, and the same buckets per client. Without asOfDate it is as of today, from the invoice balances the Sales pages show. With asOfDate it is what was owed at the end of that day, following ledger dates (invoice date, receipt date, void posting date), so its total can be compared with the receivables control account in the balance sheet at the same date; unpostedTotal is the part owed on invoices that have no ledger posting. Drafts and voided invoices are excluded. Requires the Sales module and permission to view invoices.")]
        public async Task<WorkspaceDataMcpResponse> GetReceivablesAging(
            [Description("Optional client id, like 'cus_…', to age one client only.")] string? clientId = null,
            [Description("Optional date to age as of, e.g. a period end '2026-12-31'. Defaults to today.")] string? asOfDate = null,
            CancellationToken cancellationToken = default)
        {
            var asOf = ParseDate(asOfDate, nameof(asOfDate));
            var resolved = await ResolveIdAsync("receivablesAging", clientId, "cus", nameof(clientId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var clientKey = resolved.Id;
            if (asOf.HasValue)
            {
                var past = await Reader.ReadAsync<SalesController>(nameof(SalesController.ReceivablesAsOf), c => c.ReceivablesAsOf(asOf.Value, clientKey),
                    new Dictionary<string, object?> { ["asOf"] = asOf.Value, ["customerId"] = clientKey });
                if (!past.Succeeded) return Refused("receivablesAging", past);

                var items = ((IEnumerable<Dto.Financial.OpenItemAsOf>?)past.Value ?? Array.Empty<Dto.Financial.OpenItemAsOf>()).ToList();
                await PrefetchAsync(items.Select(i => ("cus", i.PartyId)).Distinct(), cancellationToken);
                return Success("receivablesAging",
                    Aging.Build(items.Select(i => new AgingInput(i.PartyId, i.PartyName, i.Id, i.No, i.DueDate, i.BalanceDue)).ToList(),
                        asOf.Value.Date, id => Pub("cus", id), id => PagePath($"sales/clients/{Pub("cus", id)}"),
                        items.Where(i => !i.Posted).Sum(i => i.BalanceDue)),
                    link: PagePath("sales"));
            }

            var read = await Reader.ReadAsync<SalesController>(nameof(SalesController.SalesInvoices), c => c.SalesInvoices(clientKey),
                new Dictionary<string, object?> { ["customerId"] = clientKey });
            if (!read.Succeeded) return Refused("receivablesAging", read);

            var open = ((IEnumerable<Dto.Sales.SalesInvoice>?)read.Value ?? Array.Empty<Dto.Sales.SalesInvoice>())
                .Where(i => !SameStatus(i.Status, "Draft") && !SameStatus(i.Status, "Void") && i.BalanceDue > 0.005m)
                .Select(i => new AgingInput(i.CustomerId, i.CustomerName, i.Id, i.No, i.DueDate ?? i.InvoiceDate, i.BalanceDue))
                .ToList();
            await PrefetchAsync(open.Select(i => ("cus", i.PartyId)).Distinct(), cancellationToken);
            return Success("receivablesAging",
                Aging.Build(open, DateTime.UtcNow.Date, id => Pub("cus", id), id => PagePath($"sales/clients/{Pub("cus", id)}")),
                link: PagePath("sales"));
        }

        [McpServerTool(Name = "get_recurring_schedules", Title = "Get Recurring Invoice Schedules", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Without scheduleId: lists recurring invoice schedules (client, frequency, next run, status), optionally by status or only those needing attention. With scheduleId: that schedule's lines, upcoming dates and the history of invoices it created. Requires the Sales module and permission to view invoices.")]
        public async Task<WorkspaceDataMcpResponse> GetRecurringSchedules(
            [Description("Optional schedule id, like 'rsch_…', for the full detail and history.")] string? scheduleId = null,
            [Description("Optional status filter for the list (e.g. Active, Paused, Ended).")] string? status = null,
            [Description("Only schedules that need attention. Defaults to false.")] bool needsAttention = false,
            CancellationToken cancellationToken = default)
        {
            var resolved = await ResolveIdAsync("recurringSchedule", scheduleId, "rsch", nameof(scheduleId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            if (resolved.Id is int id)
            {
                var detail = await Reader.ReadAsync<RecurringSchedulesController>(nameof(RecurringSchedulesController.Get), c => c.Get(id),
                    new Dictionary<string, object?> { ["id"] = id });
                if (!detail.Succeeded) return Refused("recurringSchedule", detail);
                var history = await Reader.ReadAsync<RecurringSchedulesController>(nameof(RecurringSchedulesController.Occurrences), c => c.Occurrences(id),
                    new Dictionary<string, object?> { ["id"] = id });
                return Success("recurringSchedule", new { Schedule = detail.Value, Occurrences = history.Succeeded ? history.Value : null },
                    link: PagePath($"sales/recurring/{Pub("rsch", id)}"));
            }

            return await RecordAsync<RecurringSchedulesController>("recurringSchedules", nameof(RecurringSchedulesController.List),
                c => c.List(status, needsAttention), link: PagePath("sales/recurring"),
                arguments: new Dictionary<string, object?> { ["status"] = status, ["needsAttention"] = needsAttention });
        }

        private static IEnumerable<(string Prefix, int Id)> InvoiceIds(IEnumerable<Dto.Sales.SalesInvoice> invoices) =>
            invoices.SelectMany(i => new[] { ("inv", i.Id), ("cus", i.CustomerId) });

        private object InvoiceSummary(Dto.Sales.SalesInvoice i) => new
        {
            Id = Pub("inv", i.Id),
            i.No,
            ClientId = Pub("cus", i.CustomerId),
            ClientName = i.CustomerName,
            i.InvoiceDate,
            i.DueDate,
            i.Status,
            Total = i.TotalAmountAfterTax ?? i.Amount,
            i.PaidAmount,
            i.BalanceDue,
            i.Posted,
            i.ReferenceNo,
            Link = PagePath($"sales/{Pub("inv", i.Id)}")
        };
    }

    internal sealed record AgingInput(int PartyId, string? PartyName, int DocumentId, string? DocumentNo, DateTime DueDate, decimal Balance);

    /// <summary>Buckets open balances by days past due.</summary>
    internal static class Aging
    {
        private static readonly string[] Buckets = { "current", "days1To30", "days31To60", "days61To90", "over90" };

        public static object Build(IReadOnlyCollection<AgingInput> open, DateTime today, Func<int, string?> partyPublicId, Func<int, string> partyLink, decimal? unpostedTotal = null)
        {
            static int BucketOf(DateTime due, DateTime today)
            {
                var days = (today - due.Date).Days;
                return days <= 0 ? 0 : days <= 30 ? 1 : days <= 60 ? 2 : days <= 90 ? 3 : 4;
            }

            Dictionary<string, decimal> Totals(IEnumerable<AgingInput> rows)
            {
                var totals = Buckets.ToDictionary(b => b, _ => 0m);
                foreach (var row in rows)
                    totals[Buckets[BucketOf(row.DueDate, today)]] += row.Balance;
                return totals;
            }

            return new
            {
                AsOf = today,
                Total = open.Sum(r => r.Balance),
                UnpostedTotal = unpostedTotal,
                Buckets = Totals(open),
                DocumentCount = open.Count,
                Parties = open
                    .GroupBy(r => r.PartyId)
                    .Select(g => new
                    {
                        Id = partyPublicId(g.Key),
                        Name = g.First().PartyName,
                        Total = g.Sum(r => r.Balance),
                        Buckets = Totals(g),
                        OldestDueDate = g.Min(r => r.DueDate),
                        Documents = g.Count(),
                        Link = partyLink(g.Key)
                    })
                    .OrderByDescending(p => p.Total)
                    .ToList()
            };
        }
    }
}

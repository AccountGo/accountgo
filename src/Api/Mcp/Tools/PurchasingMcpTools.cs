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
    /// <summary>Vendors, bills and payables aging, as the Purchases pages show them.</summary>
    [McpServerToolType]
    public sealed class PurchasingMcpTools : WorkspaceMcpToolBase
    {
        public PurchasingMcpTools(McpControllerReader reader, ITenantContext tenant) : base(reader, tenant)
        {
        }

        [McpServerTool(Name = "search_vendors", Title = "Search Vendors", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists the workspace's vendors (suppliers) with what the workspace owes each: outstanding, overdue, paid to date and last bill date. Filter by name/email/phone text, include archived vendors, or only vendors with a balance. Use get_vendor for contacts and recent bills. Requires the Purchases module and permission to view vendors; returns Status PlanRestricted or AccessDenied instead of throwing.")]
        public async Task<WorkspaceDataMcpResponse> SearchVendors(
            [Description("Optional text matched against vendor name, email and phone.")] string? query = null,
            [Description("Only vendors the workspace currently owes. Defaults to false.")] bool withBalanceOnly = false,
            [Description("Include archived vendors. Defaults to false.")] bool includeArchived = false,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Vendors), c => c.Vendors());
            if (!read.Succeeded) return Refused("vendors", read);

            var vendors = ((IEnumerable<Dto.Purchasing.Supplier>?)read.Value ?? Array.Empty<Dto.Purchasing.Supplier>())
                .Where(v => includeArchived || !v.Archived)
                .Where(v => !withBalanceOnly || v.Outstanding > 0)
                .Where(v => Matches(query, v.Name, v.Email, v.Phone))
                .ToList();
            return await PageAsync("vendors", vendors, skip, take,
                v => new[] { ("ven", v.Id) },
                v => new
                {
                    Id = Pub("ven", v.Id),
                    v.Name,
                    v.Email,
                    v.Phone,
                    v.Country,
                    v.Archived,
                    v.Outstanding,
                    v.Overdue,
                    v.PaidToDate,
                    v.LastBillDate,
                    Link = PagePath($"purchases/vendors/{Pub("ven", v.Id)}")
                },
                PagePath("purchases/vendors"), cancellationToken);
        }

        [McpServerTool(Name = "get_vendor", Title = "Get Vendor", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one vendor's details (address, contacts, payment term, balances) plus their most recent bills with status and balance due. Requires the Purchases module and permission to view vendors; bills are included only when the member may view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetVendor(
            [Description("The vendor id, like 'ven_…' (from search_vendors or a bill's vendorId).")] string vendorId,
            [Description("How many recent bills to include (0-50). Defaults to 10.")] int recentBills = 10,
            CancellationToken cancellationToken = default)
        {
            if (recentBills < 0 || recentBills > 50)
                throw new ArgumentException("recentBills must be between 0 and 50.", nameof(recentBills));

            var resolved = await ResolveIdAsync("vendor", vendorId, "ven", nameof(vendorId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            if (resolved.Id is not int vendorKey) return Invalid("vendor", "Provide vendorId.");

            var vendor = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Vendor), c => c.Vendor(vendorKey),
                new Dictionary<string, object?> { ["id"] = vendorKey });
            if (!vendor.Succeeded) return Refused("vendor", vendor);

            object? bills = null;
            string? billsNote = null;
            if (recentBills > 0)
            {
                var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorKey),
                    new Dictionary<string, object?> { ["vendorId"] = vendorKey });
                if (read.Succeeded)
                {
                    var all = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>()).ToList();
                    var recent = all.Take(recentBills).ToList();
                    await PrefetchAsync(BillIds(recent), cancellationToken);
                    bills = new { TotalCount = all.Count, Items = recent.Select(BillSummary).ToList() };
                }
                else
                {
                    billsNote = read.Message;
                }
            }

            return Success("vendor", new { Vendor = vendor.Value, RecentBills = bills, RecentBillsNote = billsNote },
                link: PagePath($"purchases/vendors/{Pub("ven", vendorKey)}"));
        }

        [McpServerTool(Name = "search_bills", Title = "Search Bills", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists vendor bills newest first with number, vendor and vendor invoice number, dates, status (Draft, Open, Overdue, Partially Paid, Paid, Void), total, amount paid and balance due. Filter by vendor, status, date range, overdue only, or text in number/vendor/vendor invoice number/reference. Use get_bill for lines, expense accounts and payments. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> SearchBills(
            [Description("Optional text matched against bill number, vendor name, vendor invoice number and reference.")] string? query = null,
            [Description("Optional vendor id, like 'ven_…'.")] string? vendorId = null,
            [Description("Optional status: Draft, Open, Overdue, Partially Paid, Paid or Void.")] string? status = null,
            [Description("Only bills that are past due with a balance. Defaults to false.")] bool overdueOnly = false,
            [Description("Optional earliest bill date (e.g. '2026-01-01').")] string? fromDate = null,
            [Description("Optional latest bill date (e.g. '2026-12-31').")] string? toDate = null,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate));
            var resolved = await ResolveIdAsync("bills", vendorId, "ven", nameof(vendorId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var vendorKey = resolved.Id;

            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorKey),
                new Dictionary<string, object?> { ["vendorId"] = vendorKey });
            if (!read.Succeeded) return Refused("bills", read);

            var bills = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>())
                .Where(b => SameStatus(b.Status, status))
                .Where(b => !overdueOnly || SameStatus(b.Status, "Overdue"))
                .Where(b => from == null || b.Date.Date >= from)
                .Where(b => to == null || b.Date.Date <= to)
                .Where(b => Matches(query, b.No, b.VendorName, b.VendorInvoiceNo, b.ReferenceNo))
                .ToList();
            return await PageAsync("bills", bills, skip, take, b => BillIds(new[] { b }), BillSummary,
                PagePath("purchases"), cancellationToken);
        }

        [McpServerTool(Name = "get_bill", Title = "Get Bill", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one vendor bill in full: vendor, vendor invoice number, dates and due date, status, lines (description, expense account, quantity, amount, tax), totals, payments, balance due, approval and void details. Look it up by id like 'bill_…' (from search_bills or a GL transaction's sourceDocumentId for document type PurchaseInvoice) or by bill number. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetBill(
            [Description("The bill id, like 'bill_…'. Provide this or billNo.")] string? billId = null,
            [Description("The bill number or the vendor's invoice number. Provide this or billId.")] string? billNo = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(billId) && string.IsNullOrWhiteSpace(billNo))
                return Invalid("bill", "Provide billId or billNo.");

            var resolved = await ResolveIdAsync("bill", billId, "bill", nameof(billId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var id = resolved.Id;
            if (id == null)
            {
                var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(null),
                    new Dictionary<string, object?> { ["vendorId"] = null });
                if (!read.Succeeded) return Refused("bill", read);
                var wanted = billNo!.Trim();
                var all = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>()).ToList();
                id = (all.FirstOrDefault(b => string.Equals(b.No?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                      ?? all.FirstOrDefault(b => string.Equals(b.VendorInvoiceNo?.Trim(), wanted, StringComparison.OrdinalIgnoreCase)))?.Id;
                if (id == null)
                    return NotFound("bill", $"No bill numbered '{billNo}' in this workspace.");
            }

            return await RecordAsync<PurchasingController>("bill", nameof(PurchasingController.Bill), c => c.Bill(id.Value),
                link: PagePath($"purchases/{Pub("bill", id.Value)}"), arguments: new Dictionary<string, object?> { ["id"] = id });
        }

        [McpServerTool(Name = "get_payables_aging", Title = "Get Payables Aging", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Accounts payable aging: totals in Current, 1-30, 31-60, 61-90 and over 90 days past due, and the same buckets per vendor. Without asOfDate it is as of today, from the bill balances the Purchases pages show. With asOfDate it is what was owed at the end of that day, following ledger dates (bill date, payment date, void posting date), so its total can be compared with the payables control account in the balance sheet at the same date; unpostedTotal is the part owed on bills that have no ledger posting. Drafts and voided bills are excluded. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetPayablesAging(
            [Description("Optional vendor id, like 'ven_…', to age one vendor only.")] string? vendorId = null,
            [Description("Optional date to age as of, e.g. a period end '2026-12-31'. Defaults to today.")] string? asOfDate = null,
            CancellationToken cancellationToken = default)
        {
            var asOf = ParseDate(asOfDate, nameof(asOfDate));
            var resolved = await ResolveIdAsync("payablesAging", vendorId, "ven", nameof(vendorId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var vendorKey = resolved.Id;
            if (asOf.HasValue)
            {
                var past = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.PayablesAsOf), c => c.PayablesAsOf(asOf.Value, vendorKey),
                    new Dictionary<string, object?> { ["asOf"] = asOf.Value, ["vendorId"] = vendorKey });
                if (!past.Succeeded) return Refused("payablesAging", past);

                var items = ((IEnumerable<Dto.Financial.OpenItemAsOf>?)past.Value ?? Array.Empty<Dto.Financial.OpenItemAsOf>()).ToList();
                await PrefetchAsync(items.Select(i => ("ven", i.PartyId)).Distinct(), cancellationToken);
                return Success("payablesAging",
                    Aging.Build(items.Select(i => new AgingInput(i.PartyId, i.PartyName, i.Id, i.No, i.DueDate, i.BalanceDue)).ToList(),
                        asOf.Value.Date, id => Pub("ven", id), id => PagePath($"purchases/vendors/{Pub("ven", id)}"),
                        items.Where(i => !i.Posted).Sum(i => i.BalanceDue)),
                    link: PagePath("purchases"));
            }

            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorKey),
                new Dictionary<string, object?> { ["vendorId"] = vendorKey });
            if (!read.Succeeded) return Refused("payablesAging", read);

            var open = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>())
                .Where(b => !SameStatus(b.Status, "Draft") && !SameStatus(b.Status, "Void") && b.BalanceDue > 0.005m)
                .Select(b => new AgingInput(b.VendorId, b.VendorName, b.Id, b.No, b.DueDate ?? b.Date, b.BalanceDue))
                .ToList();
            await PrefetchAsync(open.Select(b => ("ven", b.PartyId)).Distinct(), cancellationToken);
            return Success("payablesAging",
                Aging.Build(open, DateTime.UtcNow.Date, id => Pub("ven", id), id => PagePath($"purchases/vendors/{Pub("ven", id)}")),
                link: PagePath("purchases"));
        }

        [McpServerTool(Name = "search_bill_payments", Title = "Search Bill Payments", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists payments made on vendor bills, newest first, each with its date, amount, method, reference and the bill it settled (bill number, vendor invoice number, bill date, vendor). Filter by payment date range or vendor. Use it to see what was paid after a period end and which period the bill belongs to. Voided payments are left out unless includeVoided is true. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> SearchBillPayments(
            [Description("Optional vendor id, like 'ven_…'.")] string? vendorId = null,
            [Description("Optional earliest payment date (e.g. '2027-01-01').")] string? fromDate = null,
            [Description("Optional latest payment date (e.g. '2027-01-31').")] string? toDate = null,
            [Description("Include voided payments. Defaults to false.")] bool includeVoided = false,
            [Description("Records to skip. Defaults to 0.")] int skip = 0,
            [Description("Records to return (1-100). Defaults to 25.")] int take = DefaultTake,
            CancellationToken cancellationToken = default)
        {
            ValidatePaging(skip, take);
            var from = ParseDate(fromDate, nameof(fromDate));
            var to = ParseDate(toDate, nameof(toDate));
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ArgumentException("The earliest date must not be after the latest date.", nameof(fromDate));
            var resolved = await ResolveIdAsync("billPayments", vendorId, "ven", nameof(vendorId), cancellationToken);
            if (resolved.Failure != null) return resolved.Failure;
            var vendorKey = resolved.Id;

            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorKey),
                new Dictionary<string, object?> { ["vendorId"] = vendorKey });
            if (!read.Succeeded) return Refused("billPayments", read);

            var payments = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>())
                .SelectMany(b => (b.Payments ?? new List<Dto.Purchasing.BillPayment>()).Select(p => new { Bill = b, Payment = p }))
                .Where(x => includeVoided || !x.Payment.IsVoid)
                .Where(x => (!from.HasValue || x.Payment.Date.Date >= from.Value) && (!to.HasValue || x.Payment.Date.Date <= to.Value))
                .OrderByDescending(x => x.Payment.Date).ThenByDescending(x => x.Payment.Id)
                .ToList();
            return await PageAsync("billPayments", payments, skip, take,
                x => new[] { ("vpay", x.Payment.Id), ("bill", x.Bill.Id), ("ven", x.Bill.VendorId) },
                x => new
                {
                    Id = Pub("vpay", x.Payment.Id),
                    x.Payment.No,
                    x.Payment.Date,
                    x.Payment.Amount,
                    x.Payment.PaymentMethod,
                    x.Payment.ReferenceNo,
                    x.Payment.AccountName,
                    x.Payment.IsVoid,
                    x.Payment.VoidedAt,
                    BillId = Pub("bill", x.Bill.Id),
                    BillNo = x.Bill.No,
                    x.Bill.VendorInvoiceNo,
                    BillDate = x.Bill.Date,
                    VendorId = Pub("ven", x.Bill.VendorId),
                    x.Bill.VendorName,
                    Link = PagePath($"purchases/{Pub("bill", x.Bill.Id)}")
                },
                PagePath("purchases"), cancellationToken);
        }

        private static IEnumerable<(string Prefix, int Id)> BillIds(IEnumerable<Dto.Purchasing.Bill> bills) =>
            bills.SelectMany(b => new[] { ("bill", b.Id), ("ven", b.VendorId) });

        private object BillSummary(Dto.Purchasing.Bill b) => new
        {
            Id = Pub("bill", b.Id),
            b.No,
            VendorId = Pub("ven", b.VendorId),
            b.VendorName,
            b.VendorInvoiceNo,
            b.Date,
            b.DueDate,
            b.Status,
            b.Total,
            b.AmountPaid,
            b.BalanceDue,
            b.ReferenceNo,
            Link = PagePath($"purchases/{Pub("bill", b.Id)}")
        };
    }
}

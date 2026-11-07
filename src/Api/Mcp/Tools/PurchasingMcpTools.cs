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
                .Select(v => new
                {
                    v.Id,
                    v.Name,
                    v.Email,
                    v.Phone,
                    v.Country,
                    v.Archived,
                    v.Outstanding,
                    v.Overdue,
                    v.PaidToDate,
                    v.LastBillDate,
                    Link = PagePath($"purchases/vendors/{v.Id}")
                })
                .ToList();
            return Page("vendors", vendors, skip, take, PagePath("purchases/vendors"));
        }

        [McpServerTool(Name = "get_vendor", Title = "Get Vendor", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one vendor's details (address, contacts, payment term, balances) plus their most recent bills with status and balance due. Requires the Purchases module and permission to view vendors; bills are included only when the member may view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetVendor(
            [Description("The vendor id (from search_vendors or a bill's vendorId).")] int vendorId,
            [Description("How many recent bills to include (0-50). Defaults to 10.")] int recentBills = 10,
            CancellationToken cancellationToken = default)
        {
            if (recentBills < 0 || recentBills > 50)
                throw new ArgumentException("recentBills must be between 0 and 50.", nameof(recentBills));

            var vendor = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Vendor), c => c.Vendor(vendorId),
                new Dictionary<string, object?> { ["id"] = vendorId });
            if (!vendor.Succeeded) return Refused("vendor", vendor);

            object? bills = null;
            string? billsNote = null;
            if (recentBills > 0)
            {
                var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorId),
                    new Dictionary<string, object?> { ["vendorId"] = vendorId });
                if (read.Succeeded)
                {
                    var all = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>()).ToList();
                    bills = new { TotalCount = all.Count, Items = all.Take(recentBills).Select(BillSummary).ToList() };
                }
                else
                {
                    billsNote = read.Message;
                }
            }

            return Success("vendor", new { Vendor = vendor.Value, RecentBills = bills, RecentBillsNote = billsNote },
                link: PagePath($"purchases/vendors/{vendorId}"));
        }

        [McpServerTool(Name = "search_bills", Title = "Search Bills", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Lists vendor bills newest first with number, vendor and vendor invoice number, dates, status (Draft, Open, Overdue, Partially Paid, Paid, Void), total, amount paid and balance due. Filter by vendor, status, date range, overdue only, or text in number/vendor/vendor invoice number/reference. Use get_bill for lines, expense accounts and payments. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> SearchBills(
            [Description("Optional text matched against bill number, vendor name, vendor invoice number and reference.")] string? query = null,
            [Description("Optional vendor id.")] int? vendorId = null,
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

            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorId),
                new Dictionary<string, object?> { ["vendorId"] = vendorId });
            if (!read.Succeeded) return Refused("bills", read);

            var bills = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>())
                .Where(b => SameStatus(b.Status, status))
                .Where(b => !overdueOnly || SameStatus(b.Status, "Overdue"))
                .Where(b => from == null || b.Date.Date >= from)
                .Where(b => to == null || b.Date.Date <= to)
                .Where(b => Matches(query, b.No, b.VendorName, b.VendorInvoiceNo, b.ReferenceNo))
                .Select(BillSummary)
                .ToList();
            return Page("bills", bills, skip, take, PagePath("purchases"));
        }

        [McpServerTool(Name = "get_bill", Title = "Get Bill", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Returns one vendor bill in full: vendor, vendor invoice number, dates and due date, status, lines (description, expense account, quantity, amount, tax), totals, payments, balance due, approval and void details. Look it up by id (from search_bills or a GL transaction's sourceDocumentId for document type PurchaseInvoice) or by bill number. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetBill(
            [Description("The bill id. Provide this or billNo.")] int? billId = null,
            [Description("The bill number or the vendor's invoice number. Provide this or billId.")] string? billNo = null,
            CancellationToken cancellationToken = default)
        {
            if (billId == null && string.IsNullOrWhiteSpace(billNo))
                return Invalid("bill", "Provide billId or billNo.");

            var id = billId;
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
                link: PagePath($"purchases/{id}"), arguments: new Dictionary<string, object?> { ["id"] = id });
        }

        [McpServerTool(Name = "get_payables_aging", Title = "Get Payables Aging", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
        [Description("Accounts payable aging as of today, from the same bill balances and due dates the Purchases pages show: totals in Current, 1-30, 31-60, 61-90 and over 90 days past due, and the same buckets per vendor. Drafts and voided bills are excluded. Requires the Purchases module and permission to view bills.")]
        public async Task<WorkspaceDataMcpResponse> GetPayablesAging(
            [Description("Optional vendor id to age one vendor only.")] int? vendorId = null,
            CancellationToken cancellationToken = default)
        {
            var read = await Reader.ReadAsync<PurchasingController>(nameof(PurchasingController.Bills), c => c.Bills(vendorId),
                new Dictionary<string, object?> { ["vendorId"] = vendorId });
            if (!read.Succeeded) return Refused("payablesAging", read);

            var open = ((IEnumerable<Dto.Purchasing.Bill>?)read.Value ?? Array.Empty<Dto.Purchasing.Bill>())
                .Where(b => !SameStatus(b.Status, "Draft") && !SameStatus(b.Status, "Void") && b.BalanceDue > 0.005m)
                .Select(b => new AgingInput(b.VendorId, b.VendorName, b.Id, b.No, b.DueDate ?? b.Date, b.BalanceDue))
                .ToList();
            return Success("payablesAging", Aging.Build(open, DateTime.UtcNow.Date, id => PagePath($"purchases/vendors/{id}")),
                link: PagePath("purchases"));
        }

        private object BillSummary(Dto.Purchasing.Bill b) => new
        {
            b.Id,
            b.No,
            b.VendorId,
            b.VendorName,
            b.VendorInvoiceNo,
            b.Date,
            b.DueDate,
            b.Status,
            b.Total,
            b.AmountPaid,
            b.BalanceDue,
            b.ReferenceNo,
            Link = PagePath($"purchases/{b.Id}")
        };
    }
}

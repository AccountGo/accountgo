using System;
using System.Collections.Generic;

namespace Dto.Purchasing
{
    /// <summary>
    /// A bill as the Bills pages show and save it. Totals, balance and status are worked out on the server; saving
    /// sends only the header and lines.
    /// </summary>
    public class Bill
    {
        public int Id { get; set; }
        /// <summary>The workspace's own number, given when the bill is approved.</summary>
        public string? No { get; set; }
        public int VendorId { get; set; }
        /// <summary>The vendor as recorded when the bill was approved, or as it is now on drafts.</summary>
        public string? VendorName { get; set; }
        public string? VendorEmail { get; set; }
        public string? VendorPhone { get; set; }
        public string? VendorAddress { get; set; }
        /// <summary>The vendor's own number for the bill.</summary>
        public string? VendorInvoiceNo { get; set; }
        public DateTime Date { get; set; }
        /// <summary>When saving: a due date set by hand, or null to follow the payment term.</summary>
        public DateTime? DueDate { get; set; }
        public int? PaymentTermId { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Notes { get; set; }
        public List<BillLine> Lines { get; set; } = new();

        public decimal Subtotal { get; set; }
        public decimal TotalTax { get; set; }
        public decimal Total { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal BalanceDue { get; set; }
        /// <summary>
        /// Draft, Open, Overdue, Partially Paid, Paid or Void. When saving, "Open" approves the bill (which needs the
        /// post permission); anything else saves a draft.
        /// </summary>
        public string? Status { get; set; }
        public bool Approved { get; set; }
        public List<BillPayment> Payments { get; set; } = new();
    }

    public class BillLine
    {
        /// <summary>The line's id when editing a draft; 0 for a new line.</summary>
        public int Id { get; set; }
        /// <summary>A catalog item, or null for a line typed without one.</summary>
        public int? ItemId { get; set; }
        public string? Description { get; set; }
        /// <summary>The account the line is booked to; null for the default (the vendor's purchase account).</summary>
        public int? AccountId { get; set; }
        public string? AccountName { get; set; }
        public decimal Quantity { get; set; } = 1;
        /// <summary>Unit cost.</summary>
        public decimal Amount { get; set; }
        /// <summary>Percent off.</summary>
        public decimal? Discount { get; set; }
        public int? TaxId { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal Subtotal { get; set; }
    }

    public class BillPayment
    {
        public int Id { get; set; }
        public string? No { get; set; }
        public DateTime Date { get; set; }
        public decimal Amount { get; set; }
        public int? AccountId { get; set; }
        /// <summary>The cash or bank account it was paid from.</summary>
        public string? AccountName { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
    }

    /// <summary>A payment on one bill.</summary>
    public class PayBill
    {
        public int BillId { get; set; }
        public decimal Amount { get; set; }
        public DateTime? Date { get; set; }
        /// <summary>The cash or bank account paid from; null for the workspace's first bank account.</summary>
        public int? AccountId { get; set; }
        public string? PaymentMethod { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Memo { get; set; }
    }

    /// <summary>An account a bill line can be booked to, or a payment made from.</summary>
    public class BillAccount
    {
        public int Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        /// <summary>Expense or Asset, to group the picker.</summary>
        public string? Group { get; set; }
    }
}

//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoiceHeader.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Core.Domain.Financials;
using Core.Domain.Sales;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    /// <summary>
    /// A bill: a vendor's invoice to the workspace. Drafted, then approved, which posts it to Accounts Payable and gives
    /// it the workspace's own number; paid with vendor payments, or voided, which reverses what approving posted.
    /// </summary>
    [Table("PurchaseInvoiceHeader")]
    public partial class PurchaseInvoiceHeader : BaseEntity, ICompanyOwned
    {
        public PurchaseInvoiceHeader()
        {
            PurchaseInvoiceLines = new HashSet<PurchaseInvoiceLine>();
            PurchaseOrders = new HashSet<PurchaseOrderHeader>();
            VendorPayments = new HashSet<VendorPayment>();
        }
        
        public int CompanyId { get; set; }
        public int? VendorId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        /// <summary>The entry that reversed the bill's posting when it was voided.</summary>
        public int? VoidGeneralLedgerHeaderId { get; set; }
        public DateTime Date { get; set; }
        /// <summary>The workspace's own number, given when the bill is approved; null on drafts.</summary>
        public string? No { get; set; }
        /// <summary>The vendor's number for the bill; empty when it has none.</summary>
        [Required(AllowEmptyStrings = true)]
        public string VendorInvoiceNo { get; set; } = "";
        public string? Description { get; set; }
        public int? PaymentTermId { get; set; }
        /// <summary>Stored since bills were rebuilt; null on bills saved before that (see <see cref="ResolveDueDate"/>).</summary>
        public DateTime? DueDate { get; set; }
        public string? ReferenceNo { get; set; }
        public string? Notes { get; set; }
        /// <summary>The vendor as it was when the bill was approved; drafts follow the vendor.</summary>
        public string? BillFromJson { get; set; }
        public PurchaseInvoiceStatus? Status { get; set; }

        public virtual Vendor? Vendor { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }

        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public virtual ICollection<PurchaseOrderHeader> PurchaseOrders { get; set; }
        public virtual ICollection<VendorPayment> VendorPayments { get; set; }

        public bool IsDraft => Status is null or PurchaseInvoiceStatus.Draft && GeneralLedgerHeaderId == null;
        public bool IsVoid => Status == PurchaseInvoiceStatus.Void;
        /// <summary>Approved and not voided: what the workspace owes the vendor for it counts as payable.</summary>
        public bool IsPayable => !IsDraft && !IsVoid;

        public decimal Subtotal() => PurchaseInvoiceLines.Sum(l => l.Subtotal);

        public decimal TotalTax() => PurchaseInvoiceLines.Sum(l => l.TaxAmount);

        public decimal Total() => Subtotal() + TotalTax();

        public decimal AmountPaid() => VendorPayments?.Sum(p => p.Amount) ?? 0;

        public decimal BalanceDue() => Math.Max(0, Total() - AmountPaid());

        /// <summary>
        /// The stored due date, or for bills saved before due dates were stored, the term's days after the bill date (30
        /// without a term), as invoices do.
        /// </summary>
        public DateTime ResolveDueDate(PaymentTerm? term) =>
            DueDate ?? Date.AddDays(term?.DueAfterDays ?? 30);

        /// <summary>The status a bill shows: void, paid, partly paid, draft, overdue, then open.</summary>
        public string DisplayStatus(DateTime dueDate, DateTime today)
        {
            var paid = AmountPaid();
            var balance = BalanceDue();
            if (IsVoid) return "Void";
            if (paid > 0 && balance <= 0.005m) return "Paid";
            if (paid > 0) return "Partially Paid";
            if (IsDraft) return "Draft";
            if (dueDate.Date < today.Date && balance > 0.005m) return "Overdue";
            return "Open";
        }

        /// <summary>Records the vendor once, when the bill is approved.</summary>
        public void CaptureBillFromIfApproved(Party? vendorParty, Contact? billToContact = null)
        {
            if (!IsPayable || BillFromJson != null)
                return;
            BillFromJson = InvoiceBillTo.FromParty(vendorParty, billToContact).ToJson();
        }
    }
}

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
    [PublicIdPrefix("bill")]
    public partial class PurchaseInvoiceHeader : BaseEntity, ICompanyOwned, IHasPublicId
    {
        public string PublicId { get; set; } = string.Empty;
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
        /// <summary>The Accounts Payable account credited when the bill was approved.</summary>
        public int? PayableAccountId { get; set; }
        public decimal CreditedAmount { get; set; }
        public decimal AdvanceAppliedAmount { get; set; }
        [ConcurrencyCheck]
        public long Version { get; set; }
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
        /// <summary>Primary source document classification chosen during draft entry; null means unspecified.</summary>
        public BillSourceDocumentType? SourceDocumentType { get; set; }
        /// <summary>The vendor as it was when the bill was approved; drafts follow the vendor.</summary>
        public string? BillFromJson { get; set; }
        public PurchaseInvoiceStatus? Status { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? VoidedBy { get; set; }
        public DateTime? VoidedAt { get; set; }
        public string? VoidReason { get; set; }

        public virtual Vendor? Vendor { get; set; }
        public virtual Account? PayableAccount { get; set; }
        public virtual GeneralLedgerHeader? GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }

        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; } = null!;
        public virtual ICollection<PurchaseOrderHeader> PurchaseOrders { get; set; } = null!;
        public virtual ICollection<VendorPayment> VendorPayments { get; set; } = null!;

        public bool IsDraft => Status is null or PurchaseInvoiceStatus.Draft && GeneralLedgerHeaderId == null;
        public bool IsVoid => Status == PurchaseInvoiceStatus.Void;
        /// <summary>Approved and not voided: what the workspace owes the vendor for it counts as payable.</summary>
        public bool IsPayable => !IsDraft && !IsVoid;

        public decimal Subtotal() => PurchaseInvoiceLines.Sum(l => l.Subtotal);

        public decimal TotalTax() => PurchaseInvoiceLines.Sum(l => l.TaxAmount);

        public decimal Total() => Subtotal() + TotalTax();

        public decimal AmountPaid() => VendorPayments?.Where(p => !p.IsVoid).Sum(p => p.Amount) ?? 0;

        public decimal TotalSettled() => AmountPaid() + CreditedAmount + AdvanceAppliedAmount;

        public decimal BalanceDue() => Math.Max(0, Total() - TotalSettled());

        /// <summary>
        /// What was still owed on this bill at the end of <paramref name="asOf"/>, following ledger dates: the bill counts
        /// from its own date, a payment from its date until the date its void was posted, and a bill void from the date
        /// its reversal was posted. Needs <see cref="VoidGeneralLedgerHeader"/> loaded on the bill and its payments.
        /// </summary>
        /// <param name="creditAndAdvanceSettledAsOf">Net dated credit and advance applications (net of reversals) as of <paramref name="asOf"/>.</param>
        public decimal BalanceDueAsOf(DateTime asOf, decimal creditAndAdvanceSettledAsOf = 0m)
        {
            var day = asOf.Date;
            if (IsDraft || Date.Date > day)
                return 0;
            if (IsVoid)
            {
                var voided = VoidGeneralLedgerHeader?.Date ?? VoidedAt;
                if (voided == null || voided.Value.Date <= day)
                    return 0;
            }

            var paid = (VendorPayments ?? new List<VendorPayment>())
                .Where(p => p.Date.Date <= day)
                .Where(p =>
                {
                    if (!p.IsVoid) return true;
                    var paymentVoided = p.VoidGeneralLedgerHeader?.Date ?? p.VoidedAt;
                    return paymentVoided != null && paymentVoided.Value.Date > day;
                })
                .Sum(p => p.Amount);
            return Math.Round(Math.Max(0, Total() - paid - creditAndAdvanceSettledAsOf), 2, MidpointRounding.AwayFromZero);
        }

        public void EnsureEditable()
        {
            if (!IsDraft)
                throw new InvalidOperationException("This bill is approved, so it can't be changed. Void it and enter it again instead.");
        }

        public void EnsureCanBeApproved()
        {
            if (GeneralLedgerHeaderId.HasValue)
                throw new InvalidOperationException("This bill is already approved.");
            if (IsVoid)
                throw new InvalidOperationException("A void bill can't be approved.");
            if (!VendorId.HasValue)
                throw new InvalidOperationException("Pick the vendor the bill is from.");
            if (PurchaseInvoiceLines.Count == 0)
                throw new InvalidOperationException("Add at least one line to the bill.");
            if (Total() <= 0)
                throw new InvalidOperationException("A bill's total must be more than zero.");
        }

        public void EnsureCanBeVoided()
        {
            if (IsVoid)
                return;
            if (IsDraft)
                throw new InvalidOperationException("Only an approved bill can be voided. Delete a draft bill instead.");
            if (VendorPayments.Any(p => !p.IsVoid) && CreditedAmount == 0 && AdvanceAppliedAmount == 0)
                throw new InvalidOperationException("This bill has payments that aren't void, so it can't be voided. Void them first.");
            if (VendorPayments.Any(p => !p.IsVoid) || CreditedAmount > 0 || AdvanceAppliedAmount > 0)
                throw new InvalidOperationException("This bill has active payments, credits, or advances, so it can't be voided. Reverse them first.");
        }

        public void EnsureCanRecordPayment(decimal amount)
        {
            if (IsVoid)
                throw new InvalidOperationException("A void bill can't be paid.");
            if (IsDraft)
                throw new InvalidOperationException("Approve the bill before paying it.");
            if (amount <= 0)
                throw new InvalidOperationException("A payment must be more than zero.");
            var balance = BalanceDue();
            if (amount > balance)
                throw new InvalidOperationException($"The payment is more than the {balance:0.00} still owed on this bill.");
        }

        /// <summary>
        /// The stored due date, or for bills saved before due dates were stored, the term's days after the bill date (30
        /// without a term), as invoices do.
        /// </summary>
        public DateTime ResolveDueDate(PaymentTerm? term) =>
            DueDate ?? Date.AddDays(term?.DueAfterDays ?? 30);

        /// <summary>The status a bill shows: void, paid, partly paid, settled, draft, overdue, then open.</summary>
        public string DisplayStatus(DateTime dueDate, DateTime today)
        {
            var paid = AmountPaid();
            var settled = TotalSettled();
            var balance = BalanceDue();
            if (IsVoid) return "Void";
            if (paid > 0 && balance <= 0.005m) return "Paid";
            if (settled > 0 && balance <= 0.005m) return "Settled";
            if (paid > 0) return "Partially Paid";
            if (settled > 0) return "Partially Settled";
            if (IsDraft) return "Draft";
            if (dueDate.Date < today.Date && balance > 0.005m) return "Overdue";
            return "Open";
        }

        public string? SettlementSummary()
        {
            var cash = AmountPaid();
            if (cash <= 0m && CreditedAmount <= 0m && AdvanceAppliedAmount <= 0m)
                return null;
            return $"Settled: cash {cash:0.00}, credits {CreditedAmount:0.00}, advances {AdvanceAppliedAmount:0.00}";
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

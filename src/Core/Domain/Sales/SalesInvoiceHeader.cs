//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Sales
{
    [Table("SalesInvoiceHeader")]
    public partial class SalesInvoiceHeader : BaseEntity, ICompanyOwned
    {
        public SalesInvoiceHeader()
        {
            SalesInvoiceLines = new HashSet<SalesInvoiceLine>();
            SalesReceipts = new HashSet<SalesReceiptHeader>();
            CustomerAllocations = new HashSet<CustomerAllocation>();
        }

        public int CompanyId { get; set; }

        public int CustomerId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        /// <summary>The entry that reversed the invoice's posting when it was voided.</summary>
        public int? VoidGeneralLedgerHeaderId { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public decimal ShippingHandlingCharge{ get; set; }
        /// <summary>Null means custom terms: the due date was set by hand.</summary>
        public int? PaymentTermId { get; set; }
        /// <summary>Stored since due dates could be set by hand; null on invoices saved before that (see <see cref="ResolveDueDate"/>).</summary>
        public DateTime? DueDate { get; set; }
        public string ReferenceNo { get; set; }
        public string? Notes { get; set; }
        public string? PaymentInstructions { get; set; }
        /// <summary>The seller as printed when the invoice was issued (<see cref="InvoiceSeller"/> JSON); null while a draft.</summary>
        public string? SellerJson { get; set; }
        /// <summary>The customer as printed when the invoice was issued (<see cref="InvoiceBillTo"/> JSON); null while a draft.</summary>
        public string? BillToJson { get; set; }
        public SalesInvoiceStatus? Status { get; set; }
        public virtual Customer Customer { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }
        public virtual GeneralLedgerHeader? VoidGeneralLedgerHeader { get; set; }

        public virtual ICollection<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        [NotMapped]
        public virtual ICollection<SalesReceiptHeader> SalesReceipts { get; set; }
        public virtual ICollection<CustomerAllocation> CustomerAllocations { get; set; }
        /// <summary>Records the seller once, when the invoice is first issued; drafts and voided drafts keep following the workspace.</summary>
        public void CaptureSellerIfIssued(Company? company, CompanySetting? setting)
        {
            if (Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void || SellerJson != null)
                return;
            SellerJson = InvoiceSeller.FromWorkspace(company, setting).ToJson();
        }

        /// <summary>Records the customer once, when the invoice is first issued; drafts keep following the customer.</summary>
        public void CaptureBillToIfIssued(Party? customerParty)
        {
            if (Status is null or SalesInvoiceStatus.Draft or SalesInvoiceStatus.Void || BillToJson != null)
                return;
            BillToJson = InvoiceBillTo.FromParty(customerParty).ToJson();
        }

        /// <summary>
        /// The stored due date, or for invoices saved before due dates were stored, the date they have always shown:
        /// the term's days as calendar days after the invoice date (30 without a term).
        /// </summary>
        public DateTime ResolveDueDate(PaymentTerm? term) =>
            DueDate ?? Date.AddDays(term?.DueAfterDays ?? 30);

        public decimal ComputeTotalTax()
        {
            decimal totalTax = 0;
            return totalTax;
        }

        public decimal ComputeTotalDiscount()
        {
            decimal totalDiscount = 0;
            return totalDiscount;
        }

        public bool IsFullPaid()
        {
            decimal totalInvoiceAmount = SalesInvoiceLines.Sum(a => a.Amount);
            decimal totalPaidAmount = 0;
            decimal totalAllocation = CustomerAllocations.Sum(a => a.Amount);
            foreach (var line in SalesInvoiceLines)
            {
                totalPaidAmount += line.GetAmountPaid();
            }
            return (totalPaidAmount + totalAllocation) >= totalInvoiceAmount;
        }

        public decimal ComputeTotalAmount()
        {
            decimal totalInvoiceAmount = 0;
            foreach (var line in SalesInvoiceLines)
            {
                totalInvoiceAmount += line.Quantity * line.Amount;
            }
            return totalInvoiceAmount;
        }
    }
}

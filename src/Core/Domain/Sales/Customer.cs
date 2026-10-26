//-----------------------------------------------------------------------
// <copyright file="Customer.cs" company="AccountGo">
// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using Core.Domain.TaxSystem;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
namespace Core.Domain.Sales
{
    [Table("Customer")]
    public partial class Customer : BaseEntity, ICompanyOwned
    {
        public Customer()
        {
            SalesInvoices = new HashSet<SalesInvoiceHeader>();
            SalesReceipts = new HashSet<SalesReceiptHeader>();
            SalesOrders = new HashSet<SalesOrderHeader>();
            CustomerAllocations = new HashSet<CustomerAllocation>();
        }

        public int CompanyId { get; set; }
        public string No { get; set; }
        public int? PartyId { get; set; }
        public int? PrimaryContactId { get; set; }
        public int? TaxGroupId { get; set; }
        public int? AccountsReceivableAccountId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? SalesDiscountAccountId { get; set; }
        public int? PromptPaymentDiscountAccountId { get; set; }
        public int? PaymentTermId { get; set; }
        public int? CustomerAdvancesAccountId { get; set; }
        /// <summary>When it was archived: hidden when picking a client, its invoices untouched. Null while active.</summary>
        public System.DateTime? ArchivedAt { get; set; }

        public virtual Party Party { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }
        public virtual Account AccountsReceivableAccount { get; set; }
        public virtual Account SalesAccount { get; set; }
        public virtual Account SalesDiscountAccount { get; set; }
        public virtual Account PromptPaymentDiscountAccount { get; set; }
        public virtual Contact PrimaryContact { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }
        public virtual Account CustomerAdvancesAccount { get; set; }
        public virtual ICollection<SalesInvoiceHeader> SalesInvoices { get; set; }
        public virtual ICollection<SalesReceiptHeader> SalesReceipts { get; set; }
        public virtual ICollection<SalesOrderHeader> SalesOrders { get; set; }
        public virtual ICollection<CustomerAllocation> CustomerAllocations { get; set; }

        public virtual IList<CustomerContact> CustomerContact { get; set; }

        [NotMapped]
        public decimal Balance { get { return GetBalance(); } }

        private decimal GetBalance()
        {
            decimal balance = 0;
            decimal totalInvoiceAmount = 0;
            decimal totalReceiptAmount = 0;

            // Drafts and voided invoices were never issued, so they are not receivables.
            totalInvoiceAmount = SalesInvoices
                .Where(i => i.Status != SalesInvoiceStatus.Void && (i.Status != SalesInvoiceStatus.Draft || i.GeneralLedgerHeaderId.HasValue))
                .Sum(i => i.SalesInvoiceLines.Sum(l => l.Amount * l.Quantity));
            totalReceiptAmount = SalesReceipts.Sum(r => r.SalesReceiptLines.Sum(l => l.Amount.GetValueOrDefault() * l.Quantity.GetValueOrDefault()));

            balance = totalInvoiceAmount - totalReceiptAmount;

            return balance;
        }
            
        //private decimal GetBalance()
        //{
        //    decimal balance = 0;
        //    decimal totalInvoiceAmount = 0;
        //    decimal totalReceiptAmount = 0;
        //    decimal totalAllocation = 0;

        //    foreach (var header in SalesInvoices)
        //    {
        //        totalInvoiceAmount += header.ComputeTotalAmount();
        //        totalAllocation += header.CustomerAllocations.Sum(a => a.Amount);

        //        foreach (var receipt in header.SalesReceipts)
        //            foreach(var receiptLine in receipt.SalesReceiptLines)
        //                totalReceiptAmount += receiptLine.AmountPaid;
        //    }

        //    balance = (totalInvoiceAmount - totalReceiptAmount) - totalAllocation;

        //    return balance;
        //}
    }
}

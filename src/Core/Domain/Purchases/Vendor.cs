//-----------------------------------------------------------------------
// <copyright file="Vendor.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using Core.Domain.TaxSystem;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Purchases
{
    [Table("Vendor")]
    public partial class Vendor : BaseEntity
    {
        public Vendor()
        {
            PurchaseOrders = new HashSet<PurchaseOrderHeader>();
            PurchaseReceipts = new HashSet<PurchaseReceiptHeader>();
            PurchaseInvoices = new HashSet<PurchaseInvoiceHeader>();
            VendorPayments = new HashSet<VendorPayment>();
        }

        public string No { get; set; }
        public int? PartyId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }        
        public int? PrimaryContactId { get; set; }
        public int? PaymentTermId { get; set; }
        public int? TaxGroupId { get; set; }

        public virtual Party Party { get; set; }
        public virtual Account AccountsPayableAccount { get; set; }
        public virtual Account PurchaseAccount { get; set; }
        public virtual Account PurchaseDiscountAccount { get; set; }
        public virtual Contact PrimaryContact { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }

        public virtual ICollection<PurchaseOrderHeader> PurchaseOrders { get; set; }
        public virtual ICollection<PurchaseReceiptHeader> PurchaseReceipts { get; set; }
        public virtual ICollection<PurchaseInvoiceHeader> PurchaseInvoices { get; set; }
        public virtual ICollection<VendorPayment> VendorPayments { get; set; }
        public virtual IList<VendorContact> VendorContact { get; set; }
        public decimal GetBalance()
        {
            decimal balance = 0;
            decimal totalInvoiceAmount = 0;
            decimal totalInvoicePayment = 0;

            foreach (var invoice in PurchaseInvoices)
                totalInvoiceAmount += invoice.PurchaseInvoiceLines.Sum(a => a.Amount * a.Quantity);

            foreach (var payment in VendorPayments)
                totalInvoicePayment += payment.Amount;

            balance = totalInvoiceAmount - totalInvoicePayment;
            return balance;
        }
    }
}

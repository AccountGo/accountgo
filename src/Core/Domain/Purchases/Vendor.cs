//-----------------------------------------------------------------------
// <copyright file="Vendor.cs" company="AccountGo">
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

namespace Core.Domain.Purchases
{
    [Table("Vendor")]
    public partial class Vendor : BaseEntity, ICompanyOwned
    {
        public Vendor()
        {
            PurchaseOrders = new HashSet<PurchaseOrderHeader>();
            PurchaseReceipts = new HashSet<PurchaseReceiptHeader>();
            PurchaseInvoices = new HashSet<PurchaseInvoiceHeader>();
            VendorPayments = new HashSet<VendorPayment>();
        }

        public int CompanyId { get; set; }
        public string No { get; set; }
        public int? PartyId { get; set; }
        public int? AccountsPayableAccountId { get; set; }
        public int? PurchaseAccountId { get; set; }
        public int? PurchaseDiscountAccountId { get; set; }        
        public int? PrimaryContactId { get; set; }
        /// <summary>Contact person used as Bill From / payee attention on bills; null uses the company party alone.</summary>
        public int? BillToContactId { get; set; }
        public int? PaymentTermId { get; set; }
        public int? PaymentMethod { get; set; }
        public int? TaxGroupId { get; set; }
        public decimal? DiscountPercentage { get; set; }
        public string Street1 { get; set; }
        public string Street2 { get; set; }
        public string City { get; set; }
        public string Province { get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }
        /// <summary>When the vendor was archived: hidden when picking a vendor, its bills untouched. Null while active.</summary>
        public System.DateTime? ArchivedAt { get; set; }

        public virtual Party Party { get; set; }
        public virtual Account AccountsPayableAccount { get; set; }
        public virtual Account PurchaseAccount { get; set; }
        public virtual Account PurchaseDiscountAccount { get; set; }
        public virtual Contact PrimaryContact { get; set; }
        public virtual Contact BillToContact { get; set; }
        public virtual PaymentTerm PaymentTerm { get; set; }
        public virtual TaxGroup TaxGroup { get; set; }

        public virtual ICollection<PurchaseOrderHeader> PurchaseOrders { get; set; }
        public virtual ICollection<PurchaseReceiptHeader> PurchaseReceipts { get; set; }
        public virtual ICollection<PurchaseInvoiceHeader> PurchaseInvoices { get; set; }
        public virtual ICollection<VendorPayment> VendorPayments { get; set; }
        public virtual IList<VendorContact> VendorContact { get; set; }
        /// <summary>What the workspace still owes the vendor on its approved bills.</summary>
        public decimal GetBalance() => PurchaseInvoices.Where(i => i.IsPayable).Sum(i => i.BalanceDue());
    }
}

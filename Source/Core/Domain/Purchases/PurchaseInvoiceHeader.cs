//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoiceHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    [Table("PurchaseInvoiceHeader")]
    public partial class PurchaseInvoiceHeader : BaseEntity
    {
        public PurchaseInvoiceHeader()
        {
            PurchaseInvoiceLines = new HashSet<PurchaseInvoiceLine>();
            PurchaseOrders = new HashSet<PurchaseOrderHeader>();
            VendorPayments = new HashSet<VendorPayment>();
        }
        
        public int? VendorId { get; set; }
        public int? GeneralLedgerHeaderId { get; set; }
        public DateTime Date { get; set; }
        public string No { get; set; }
        [Required]
        public string VendorInvoiceNo { get; set; }
        public string Description { get; set; }

        public string CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; }
        public string ModifiedBy { get; set; }
        public DateTime ModifiedOn { get; set; }

        public virtual Vendor Vendor { get; set; }
        public virtual GeneralLedgerHeader GeneralLedgerHeader { get; set; }

        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public virtual ICollection<PurchaseOrderHeader> PurchaseOrders { get; set; }
        public virtual ICollection<VendorPayment> VendorPayments { get; set; }

        public decimal GetTotalTax()
        {
            decimal totalTaxAmount = 0;
            foreach (var detail in PurchaseInvoiceLines)
            {
                totalTaxAmount += detail.LineTaxAmount;
            }
            return totalTaxAmount;
        }

        public bool IsPaid()
        {
            return PurchaseInvoiceLines.Sum(a => a.Amount) == VendorPayments.Sum(a => a.Amount);
        }
    }
}

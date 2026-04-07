//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoiceHeader.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
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
        public int? PaymentTermId { get; set; }
        public string ReferenceNo { get; set; }
        public PurchaseInvoiceStatus? Status { get; set; }

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
            if (VendorPayments != null && VendorPayments.Count > 0)
            {
                var paymentSum = VendorPayments.Sum(p => p.Amount);
                if (paymentSum == PurchaseInvoiceLines.Sum(l => l.Amount * l.Quantity))
                    return true;
            }
            return false;
        }

        public decimal AmountPaid()
        {
            if (VendorPayments != null && VendorPayments.Count > 0)
            {
                var paymentSum = VendorPayments.Sum(p => p.Amount);
                return paymentSum;
            }
            return decimal.Zero;
        }
    }
}

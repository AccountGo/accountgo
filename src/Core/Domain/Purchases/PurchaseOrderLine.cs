//-----------------------------------------------------------------------
// <copyright file="PurchaseOrderLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Purchases
{
    [Table("PurchaseOrderLine")]
    public class PurchaseOrderLine : BaseEntity
    {
        public int PurchaseOrderHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Cost { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public virtual PurchaseOrderHeader PurhcaseOrderHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }

        public PurchaseOrderLine()
        {
            PurchaseInvoiceLines = new HashSet<PurchaseInvoiceLine>();
        }

        public decimal GetRemainingQtyToInvoice()
        {
            decimal invoiced = PurchaseInvoiceLines.Sum(l => l.Quantity);
            return Quantity - invoiced;
        }
    }
}

//-----------------------------------------------------------------------
// <copyright file="SalesOrderLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Core.Domain.Sales
{
    [Table("SalesOrderLine")]
    public partial class SalesOrderLine : BaseEntity
    {
        public int SalesOrderHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public virtual SalesOrderHeader SalesOrderHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual System.Collections.Generic.ICollection<SalesInvoiceLine> SalesInvoiceLines { get; set; }

        public SalesOrderLine()
        {
            SalesInvoiceLines = new System.Collections.Generic.HashSet<SalesInvoiceLine>();
        }

        public decimal GetRemainingQtyToInvoice()
        {
            decimal invoiced = SalesInvoiceLines.Sum(l => l.Quantity);
            return Quantity - invoiced;
        }
    }
}

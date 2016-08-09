//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Core.Domain.TaxSystem;

namespace Core.Domain.Sales
{
    [Table("SalesInvoiceLine")]
    public partial class SalesInvoiceLine : BaseEntity
    {
        public SalesInvoiceLine()
        {
            SalesReceiptLines = new HashSet<SalesReceiptLine>();
        }
        public int SalesInvoiceHeaderId { get; set; }
        public int? SalesOrderLineId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public decimal Quantity { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public virtual SalesInvoiceHeader SalesInvoiceHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual InventoryControlJournal InventoryControlJournal { get; set; }
        public virtual SalesOrderLine SalesOrderLine { get; set; }
        public virtual ICollection<SalesReceiptLine> SalesReceiptLines { get; set; }

        public decimal ComputeLineTaxAmount()
        {
            decimal taxAmount = 0;
            
            return taxAmount;
        }

        public decimal GetAmountPaid()
        {
            return SalesReceiptLines.Sum(a => a.AmountPaid);
        }

        public bool IsPaid()
        {
            return Amount == SalesReceiptLines.Sum(a => a.AmountPaid);
        }
    }
}

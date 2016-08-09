//-----------------------------------------------------------------------
// <copyright file="PurchaseInvoiceLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    [Table("PurchaseInvoiceLine")]
    public partial class PurchaseInvoiceLine : BaseEntity
    {
        public int PurchaseInvoiceHeaderId { get; set; }
        public int ItemId { get; set; }
        public int MeasurementId { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public int? PurchaseOrderLineId { get; set; }
        public decimal Quantity { get; set; }
        public decimal? ReceivedQuantity { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Discount { get; set; }
        public decimal Amount { get; set; }

        public virtual PurchaseInvoiceHeader PurchaseInvoiceHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual InventoryControlJournal InventoryControlJournal { get; set; }
        public virtual PurchaseOrderLine PurchaseOrderLine { get; set; }

        [NotMapped]
        public decimal LineTaxAmount { get { return ComputeLineTaxAmount(); } }

        private decimal ComputeLineTaxAmount()
        {
            decimal? lineTaxAmount = 0;
            foreach(var tax in Item.ItemTaxGroup.ItemTaxGroupTax)
            {
                lineTaxAmount += ((tax.Tax.Rate / 100)  * (Quantity * Cost));
            }
            return lineTaxAmount.Value;
        }
    }
}

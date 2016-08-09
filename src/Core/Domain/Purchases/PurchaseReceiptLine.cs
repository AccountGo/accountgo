//-----------------------------------------------------------------------
// <copyright file="PurchaseReceiptLine.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Items;
using Core.Domain.TaxSystem;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Purchases
{
    [Table("PurchaseReceiptLine")]
    public partial class PurchaseReceiptLine : BaseEntity
    {
        public int PurchaseReceiptHeaderId { get; set; }
        public int ItemId { get; set; }
        public int? InventoryControlJournalId { get; set; }
        public int? PurchaseInvoiceLineId { get; set; }
        public int MeasurementId { get; set; }
        public decimal Quantity { get; set; }
        public decimal ReceivedQuantity { get; set; }
        public decimal Cost { get; set; }
        public decimal Discount { get; set; }
        public decimal Amount { get; set; }
        public virtual PurchaseReceiptHeader PurchaseReceiptHeader { get; set; }
        public virtual Item Item { get; set; }
        public virtual Measurement Measurement { get; set; }
        public virtual InventoryControlJournal InventoryControlJournal { get; set; }
        public virtual PurchaseInvoiceLine PurchaseInvoiceLine { get; set; }

        [NotMapped]
        public decimal LineTaxAmount { get { return 0; } }
    }
}

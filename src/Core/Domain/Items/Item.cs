//-----------------------------------------------------------------------
// <copyright file="Item.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Core.Domain.Items
{
    [Table("Item")]
    public partial class Item : BaseEntity
    {
        public Item()
        {
            SalesInvoiceLines = new HashSet<SalesInvoiceLine>();
            PurchaseOrderLines = new HashSet<PurchaseOrderLine>();
            PurchaseReceiptLines = new HashSet<PurchaseReceiptLine>();
            PurchaseInvoiceLines = new HashSet<PurchaseInvoiceLine>();
            InventoryControlJournals = new HashSet<InventoryControlJournal>();
        }

        public int? ItemCategoryId { get; set; }
        public int? SmallestMeasurementId { get; set; }
        public int? SellMeasurementId { get; set; }        
        public int? PurchaseMeasurementId { get; set; }
        public int? PreferredVendorId { get; set; }
        public int? ItemTaxGroupId { get; set; }
        public int? SalesAccountId { get; set; }
        public int? InventoryAccountId { get; set; }        
        public int? CostOfGoodsSoldAccountId { get; set; }
        public int? InventoryAdjustmentAccountId { get; set; }
        public string No { get; set; }
        public string Code { get; set; }
        public string Description { get; set; }
        public string PurchaseDescription { get; set; }
        public string SellDescription { get; set; }
        public decimal? Cost { get; set; }
        public decimal? Price { get; set; }
        public virtual ItemCategory ItemCategory { get; set; }
        public virtual ItemTaxGroup ItemTaxGroup { get; set; }
        public virtual Vendor PreferredVendor { get; set; }
        public virtual Account InventoryAccount { get; set; }
        public virtual Account SalesAccount { get; set; }
        public virtual Account CostOfGoodsSoldAccount { get; set; }
        public virtual Account InventoryAdjustmentAccount { get; set; }
        public virtual Measurement SmallestMeasurement { get; set; }
        public virtual Measurement SellMeasurement { get; set; }
        public virtual Measurement PurchaseMeasurement { get; set; }

        public virtual ICollection<SalesInvoiceLine> SalesInvoiceLines { get; set; }
        public virtual ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; }
        public virtual ICollection<PurchaseReceiptLine> PurchaseReceiptLines { get; set; }
        public virtual ICollection<PurchaseInvoiceLine> PurchaseInvoiceLines { get; set; }
        public virtual ICollection<InventoryControlJournal> InventoryControlJournals { get; set; }

        #region Not Mapped
        [NotMapped]
        public decimal ItemTaxAmountOutput { get { return ComputeItemTaxAmountOutput(); } }
        private decimal ComputeItemTaxAmountOutput()
        {
            decimal totalItemTaxAmount = 0;            
            foreach (var itemTaxGroup in ItemTaxGroup.ItemTaxGroupTax)
            {
                decimal salesPrice = (Price.Value / (1 + (itemTaxGroup.Tax.Rate / 100)));
                var taxAmount = salesPrice * (itemTaxGroup.Tax.Rate / 100);
                totalItemTaxAmount += taxAmount;
            }
            return totalItemTaxAmount;
        }

        [NotMapped]
        public decimal ItemTaxAmountInput { get { return ComputeItemTaxAmountInput(); } }
        private decimal ComputeItemTaxAmountInput()
        {
            decimal totalItemTaxAmount = 0;
            foreach (var itemTaxGroup in ItemTaxGroup.ItemTaxGroupTax)
            {
                var taxAmount = Cost.Value * (itemTaxGroup.Tax.Rate / 100);
                totalItemTaxAmount += taxAmount;
            }
            return totalItemTaxAmount;
        }
        #endregion

        private decimal _discountedPrice = 0;
        private decimal _discount = 0;
        public void AddDiscount(decimal discount)
        {
            _discount = discount;
        }

        public decimal ComputeDiscountedPrice()
        {
            _discountedPrice = Price.Value;
            if (_discount != 0)
            {
                _discountedPrice = Price.Value - ((_discount / 100) * Price.Value);
            }
            return _discountedPrice;
        }
        public decimal ComputeQuantityOnHand()
        {
            decimal inQty = 0;
            decimal outQty = 0;
            var invControlJOurnals = InventoryControlJournals.GetEnumerator();
            while (invControlJOurnals.MoveNext())
            {
                inQty += (invControlJOurnals.Current.INQty.HasValue && invControlJOurnals.Current.IsReverse == false) ? invControlJOurnals.Current.INQty.Value : 0;
                outQty += invControlJOurnals.Current.OUTQty.HasValue ? invControlJOurnals.Current.OUTQty.Value : 0;
            }
            return inQty - outQty;
        }

        public bool GLAccountsValidated()
        {
            bool validated = true;

            if (this.CostOfGoodsSoldAccount == null
                || this.InventoryAccount == null
                || this.InventoryAdjustmentAccount == null
                || this.SalesAccount == null)
            {
                validated = false;
            }

            return validated;
        }
    }
}

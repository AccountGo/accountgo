//-----------------------------------------------------------------------
// <copyright file="PurchaseLineItemsViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Web.Models.ViewModels.Purchases
{
    public class PurchaseLineItemsViewModel
    {
        public PurchaseLineItemsViewModel()
        {
            PurchaseLineItems = new List<PurchaseLineItemViewModel>();
            PurchaseLineItemsTaxes = new List<PurchaseLineItemTaxViewModel>();
            Quantity = 1;
        }

        public decimal SubTotal { get { return ComputeSubTotal(); } }
        public decimal AmountTotal { get { return ComputeAmountTotal(); } }
        public decimal AmountTax { get { return ComputeAmountTaxInput(); } }

        public IList<PurchaseLineItemViewModel> PurchaseLineItems { get; set; }
        public IList<PurchaseLineItemTaxViewModel> PurchaseLineItemsTaxes { get; set; }

        #region Fields for New Line Item
        public string ItemId { get; set; }
        public string ItemNo { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        #endregion

        private decimal ComputeSubTotal()
        {
            decimal subTotal = 0;
            foreach (var line in PurchaseLineItems)
            {
                subTotal += line.Total;
            }
            return subTotal;
        }

        private decimal ComputeAmountTotal()
        {
            decimal amountTotal = 0;
            foreach (var line in PurchaseLineItems)
            {
                amountTotal += line.Total;
            }
            return amountTotal;
        }

        private decimal ComputeAmountTaxInput()
        {
            decimal amountTax = 0;
            amountTax = PurchaseLineItemsTaxes.Sum(t => t.Amount);
            return amountTax;
        }
    }


    public class PurchaseLineItemViewModel
    {
        public PurchaseLineItemViewModel()
        { }

        public int? Id { get; set; }
        public int ItemId { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
        public string Measurement { get; set; }
        public decimal Quantity { get; set; }
        public decimal Received { get; set; }
        public decimal Price { get; set; }
        public decimal Total { get { return ComputeLineTotal(); } }

        private decimal ComputeLineTotal()
        {
            return (Quantity * Price);
        }
    }

    public class PurchaseLineItemTaxViewModel
    {
        public int TaxId { get; set; }
        public decimal TaxRate { get; set; }
        public string TaxName { get; set; }
        public decimal Amount { get; set; }
    }
}

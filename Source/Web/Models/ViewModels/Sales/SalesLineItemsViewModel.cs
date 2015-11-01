//-----------------------------------------------------------------------
// <copyright file="SalesLineItemsViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Inventory;
using System;
using System.Linq;
using System.Collections.Generic;
using Services.Financial;
namespace Web.Models.ViewModels.Sales
{
    public class SalesLineItemsViewModel
    {
        private IInventoryService _inventoryService;
        private IFinancialService _financialService;

        public SalesLineItemsViewModel()
        {
            SalesLineItems = new List<SalesLineItemViewModel>();
            SalesLineItemsTaxes = new List<SalesLineItemTaxViewModel>();
            Quantity = 1;
        }

        public SalesLineItemsViewModel(IInventoryService inventoryService, IFinancialService financialService)
            : this()
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
        }

        public decimal? ShippingCharges { get; set; }
        public decimal SubTotal { get { return ComputeSubTotal(); } }
        public decimal AmountTotal { get { return ComputeAmountTotal(); } }
        public decimal AmountTax { get { return ComputeAmountTax(); } }

        public IList<SalesLineItemViewModel> SalesLineItems { get; set; }
        public IList<SalesLineItemTaxViewModel> SalesLineItemsTaxes { get; set; }

        #region Fields for New Line Item
        public string ItemId { get; set; }
        public string ItemNo { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        #endregion

        private decimal ComputeSubTotal()
        {
            decimal subTotal = 0;
            foreach (var line in SalesLineItems)
            {
                subTotal += line.Total;
            }
            return subTotal;
        }

        private decimal ComputeAmountTotal()
        {
            decimal amountTotal = 0;
            foreach (var line in SalesLineItems)
            {
                amountTotal += line.Total;
            }
            return amountTotal;
        }

        public decimal ComputeAmountTax()
        {
            decimal amountTax = 0;
            amountTax = SalesLineItems.Sum(t => t.TaxAmount);
            return amountTax;
        }

        public void SetServiceHelpers(IInventoryService inventoryService, IFinancialService financialService)
        {
            _inventoryService = inventoryService;
            foreach(var line in SalesLineItems)
                line.SetServiceHelpers(financialService);
        }
    }

    public class SalesLineItemViewModel
    {
        private IFinancialService _financialService;
        
        public SalesLineItemViewModel()
        { }

        public SalesLineItemViewModel(IFinancialService financialService)
            :this()
        {
            _financialService = financialService;
        }

        public int? Id { get; set; }
        public int ItemId { get; set; }
        public string ItemNo { get; set; }
        public string ItemDescription { get; set; }
        public string Measurement { get; set; }
        public decimal Quantity { get; set; }
        public decimal Price { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get { return ComputeLineTotal(); } }
        public decimal TaxAmount { get { return ComputeLineTaxAmount(); } }

        private decimal ComputeLineTotal()
        {
            return (Quantity * Price) - ComputeLineDiscount();
        }

        public decimal ComputeLineDiscount()
        {
            decimal discountAmount = 0;
            if (Discount > 0)
                discountAmount = (Quantity * Price) * (Discount / 100);
            return discountAmount;
        }

        public decimal ComputeLineTaxAmount()
        {
            decimal lineTaxAmount = 0;
            if (_financialService != null)
            {
                var taxes = _financialService.ComputeOutputTax(ItemId, Quantity, Total, Discount);
                lineTaxAmount = Math.Round(taxes.Sum(t => t.Value), 2);
            }
            return lineTaxAmount;
        }

        public void SetServiceHelpers(IFinancialService financialService)
        {
            _financialService = financialService;
        }
    }

    public class SalesLineItemTaxViewModel
    {
        public decimal TaxId { get; set; }
        public decimal TaxRate { get; set; }
        public decimal TaxName { get; set; }
        public decimal Amount { get; set; }
    }
}

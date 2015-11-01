//-----------------------------------------------------------------------
// <copyright file="SalesHeaderViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Services.Inventory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Services.Financial;

namespace Web.Models.ViewModels.Sales
{
    public class SalesHeaderViewModel
    {
        IInventoryService _inventoryService;
        IFinancialService _financialService;

        public SalesHeaderViewModel()
        {
            SalesLine = new SalesLineItemsViewModel(_inventoryService, _financialService);
            Date = DateTime.Now;
        }

        public SalesHeaderViewModel(IInventoryService inventoryService, IFinancialService financialService)
            :this()
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
        }

        public DocumentTypes DocumentType { get; set; }
        public int Id { get; set; }
        public int? CustomerId { get; set; }
        public int? PaymentTermId { get; set; }
        public string Reference { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public decimal ShippingHandlingCharges { get; set; }
        public SalesLineItemsViewModel SalesLine { get; set; }

        public void SetServiceHelpers(IInventoryService inventoryService, IFinancialService financialService)
        {
            SalesLine.SetServiceHelpers(inventoryService, financialService);
        }
    }
}

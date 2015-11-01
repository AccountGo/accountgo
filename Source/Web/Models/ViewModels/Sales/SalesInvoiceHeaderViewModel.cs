//-----------------------------------------------------------------------
// <copyright file="SalesInvoiceHeaderViewModel.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------


using Services.Inventory;
using System;
using Services.Financial;
namespace Web.Models.ViewModels.Sales
{
    public class SalesInvoiceHeaderViewModel
    {
        public SalesInvoiceHeaderViewModel(IInventoryService inventoryService, IFinancialService financialService)
        {
            SalesLine = new SalesLineItemsViewModel(inventoryService, financialService);
            Date = DateTime.Now;
        }

        public int? Id { get; set; }
        public int? CustomerId { get; set; }
        public int? PaymentTermId { get; set; }
        public string Reference { get; set; }
        public string No { get; set; }
        public DateTime Date { get; set; }
        public SalesLineItemsViewModel SalesLine { get; set; }

        public void SetServiceHelpers(IInventoryService inventoryService, IFinancialService financialService)
        {
            SalesLine.SetServiceHelpers(inventoryService, financialService);
        }
    }
}

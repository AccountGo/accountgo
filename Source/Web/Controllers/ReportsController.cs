//-----------------------------------------------------------------------
// <copyright file="ReportsController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Inventory;
using Services.Sales;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Web.Mvc;
using Services.Financial;

namespace Web.Controllers
{
    public class ReportsController : BaseController, IController
    {
        private readonly ISalesService _salesService;
        private readonly IInventoryService _inventoryService;
        private readonly IFinancialService _financialService;

        public ReportsController(ISalesService salesService,
            IInventoryService inventoryService,
            IFinancialService financialService)
        {
            _salesService = salesService;
            _inventoryService = inventoryService;
            _financialService = financialService;
        }
        //
        // GET: /Reports/
        public ActionResult Index()
        {
            return View();
        }

        public ActionResult SalesOrder(int id)
        {
            var order = _salesService.GetSalesOrderById(id);
            var model = new Models.ViewModels.Sales.SalesHeaderViewModel(_inventoryService, null);
            model.Id = order.Id;
            model.CustomerId = order.CustomerId;
            model.PaymentTermId = order.PaymentTermId;
            model.Date = order.Date;
            model.Reference = order.ReferenceNo;
            model.No = order.No;

            foreach (var line in order.SalesOrderLines)
            {
                var lineItem = new Models.ViewModels.Sales.SalesLineItemViewModel(null);
                lineItem.Id = line.Id;
                lineItem.ItemId = line.ItemId;
                lineItem.ItemNo = line.Item.No;
                lineItem.ItemDescription = line.Item.Description;
                lineItem.Measurement = line.Measurement.Description;
                lineItem.Quantity = line.Quantity;
                lineItem.Discount = line.Discount;
                lineItem.Price = line.Amount;
                model.SalesLine.SalesLineItems.Add(lineItem);
            }
            
            HttpContext.Response.Clear();
            HttpContext.Response.AddHeader("Content-Type", "application/pdf");
            var html = base.RenderPartialViewToString("~/Views/Sales/SalesOrder.cshtml", model);
            HttpContext.Response.Filter = new PdfFilter(HttpContext.Response.Filter, html);
            return View(model);
        }

        public ActionResult SalesInvoice(int id)
        {
            var order = _salesService.GetSalesInvoiceById(id);
            var model = new Models.ViewModels.Sales.SalesHeaderViewModel(_inventoryService, _financialService);
            model.Id = order.Id;
            model.CustomerId = order.CustomerId;
            model.Date = order.Date;
            model.No = order.No;

            foreach (var line in order.SalesInvoiceLines)
            {
                var lineItem = new Models.ViewModels.Sales.SalesLineItemViewModel(_financialService);
                lineItem.Id = line.Id;
                lineItem.ItemId = line.ItemId;
                lineItem.ItemNo = line.Item.No;
                lineItem.ItemDescription = line.Item.Description;
                lineItem.Measurement = line.Measurement.Description;
                lineItem.Quantity = line.Quantity;
                lineItem.Discount = line.Discount;
                lineItem.Price = line.Amount;
                model.SalesLine.SalesLineItems.Add(lineItem);
            }

            HttpContext.Response.Clear();
            HttpContext.Response.AddHeader("Content-Type", "application/pdf");
            var html = base.RenderPartialViewToString("~/Views/Reports/SalesInvoice.cshtml", model);
            HttpContext.Response.Filter = new PdfFilter(HttpContext.Response.Filter, html);
            return View(model);
        }
	}
}

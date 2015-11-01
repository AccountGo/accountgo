//-----------------------------------------------------------------------
// <copyright file="AdministrationController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Inventory;
using System.Web.Mvc;
using System.Linq;
using System;
using Services.Sales;
using System.Collections.Generic;
using Core.Domain.Sales;
using Services.Administration;
using Web.Models.ViewModels.Administration;

namespace Web.Controllers
{
    [Authorize]
    public class AdministrationController : Controller
    {
        private readonly IInventoryService _inventoryService = null;
        private readonly ISalesService _salesService = null;
        private readonly IAdministrationService _administrationService = null;

        public AdministrationController(IInventoryService inventoryService,
            ISalesService salesService,
            IAdministrationService administrationService)
        {
            _inventoryService = inventoryService;
            _salesService = salesService;
            _administrationService = administrationService;
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult BulkInvoice()
        {
            var model = new Models.ViewModels.Administration.CreateBulkInvoice();
            var itemAssociationDues = (from i in _inventoryService.GetAllItems().ToList()
                                       where (i != null
                                       && i.SellDescription.ToLower().Contains("dues"))
                                       select i).FirstOrDefault();
            var day = 15;
            var month = DateTime.Now.Month == 12 ? 1 : DateTime.Now.Month + 1;
            var year = DateTime.Now.Month == 12 ? DateTime.Now.Year + 1 : DateTime.Now.Year;
            DateTime invoiceDate = new DateTime(year, month, day);
            model.InvoiceDate = invoiceDate;
            model.DueDate = new DateTime(year, month, day + 5);
            model.TotalAmountPerInvoice = (decimal)itemAssociationDues.Price;
            return View(model);
        }

        [HttpPost, ActionName("BulkInvoice")]
        [FormValueRequiredAttribute("GenerateBulkInvoice")]
        public ActionResult GenerateBulkInvoice(Models.ViewModels.Administration.CreateBulkInvoice model)
        {
            var day = 15;
            var month = DateTime.Now.Month == 12 ? 1 : DateTime.Now.Month + 1;
            var year = DateTime.Now.Month == 12 ? DateTime.Now.Year + 1 : DateTime.Now.Year;
            DateTime invoiceDate = new DateTime(year, month, day);

            var query = from c in _salesService.GetCustomers()
                        select c;
            var customers = query.ToList();

            var itemAssociationDues = (from i in _inventoryService.GetAllItems().ToList()
                                       where (i != null
                                       && i.SellDescription.ToLower().Contains("dues"))
                                       select i).FirstOrDefault();

            var invoices = new List<SalesInvoiceHeader>();

            foreach (var customer in customers)
            {
                var current = (from si in _salesService.GetSalesInvoices()
                               where si.CustomerId == customer.Id
                               && si.Date.Year == invoiceDate.Year
                               && si.Date.Month == invoiceDate.Month
                               && si.Date.Day == invoiceDate.Day
                               select si).FirstOrDefault();
                if (current != null)
                    return RedirectToAction("BulkInvoice");

                var invoiceLine = new SalesInvoiceLine();
                invoiceLine.ItemId = itemAssociationDues.Id;
                invoiceLine.Quantity = 1;
                invoiceLine.Amount = Convert.ToDecimal(itemAssociationDues.Price * invoiceLine.Quantity);
                invoiceLine.MeasurementId = itemAssociationDues.SmallestMeasurementId.Value;
                invoiceLine.CreatedBy = User.Identity.Name;
                invoiceLine.CreatedOn = DateTime.Now;
                invoiceLine.ModifiedBy = User.Identity.Name;
                invoiceLine.ModifiedOn = DateTime.Now;

                var invoice = new SalesInvoiceHeader();
                invoice.Date = invoiceDate;                
                invoice.CustomerId = customer.Id;
                invoice.CreatedBy = User.Identity.Name;
                invoice.CreatedOn = DateTime.Now;
                invoice.ModifiedBy = User.Identity.Name;
                invoice.ModifiedOn = DateTime.Now;
                invoice.SalesInvoiceLines.Add(invoiceLine);

                invoices.Add(invoice);
            }

            foreach (var invoice in invoices)
                _salesService.AddSalesInvoice(invoice, null);

            return RedirectToAction("SalesInvoices", "Sales");
        }

        public ActionResult Taxes()
        {
            var taxes = _administrationService.GetAllTaxes(false);
            var model = new Models.ViewModels.Administration.TaxesViewModel();
            foreach (var tax in taxes)
            {
                var taxVM = new Models.ViewModels.Administration.TaxViewModel();
                taxVM.IsActive = tax.IsActive;
                taxVM.PurchasingAccountId = tax.PurchasingAccountId;
                taxVM.SalesAccountId = tax.SalesAccountId;
                taxVM.TaxCode = tax.TaxCode;
                taxVM.TaxName = tax.TaxName;
                taxVM.Rate = tax.Rate;
                model.Taxes.Add(taxVM);
            }
            return View(model);
        }

        public ActionResult ItemTaxGroups()
        {
            var itemTaxGroups = _administrationService.GetItemTaxGroups(true);
            var model = new List<ItemTaxGroupViewModel>();
            foreach (var itemTax in itemTaxGroups)
            {
                var m = new ItemTaxGroupViewModel();
                m.Id = itemTax.Id;
                m.IsFullyExempt = itemTax.IsFullyExempt;
                m.Name = itemTax.Name;
                foreach(var itemTaxGroup in itemTax.ItemTaxGroupTax)
                {
                    var m_ = new ItemTaxGroupTaxViewModel();
                    m_.IsExempt = itemTaxGroup.IsExempt;
                    m_.TaxName = itemTaxGroup.Tax.TaxName;
                    m_.TaxId = itemTaxGroup.TaxId;
                    m_.Rate = itemTaxGroup.Tax.Rate;
                    m.ItemTaxGroupTaxes.Add(m_);
                }
                model.Add(m);
            }
            return View(model);
        }

        public ActionResult ItemTaxGroup(int id)
        {
            var itemTaxGroup = _administrationService.GetItemTaxGroups(true).Where(t => t.Id == id).FirstOrDefault();
            var model = new ItemTaxGroupViewModel();
            foreach (var m in itemTaxGroup.ItemTaxGroupTax)
            {
                var m_ = new ItemTaxGroupTaxViewModel();
                m_.IsExempt = m.IsExempt;
                m_.TaxName = m.Tax.TaxName;
                m_.TaxId = m.TaxId;
                m_.Rate = m.Tax.Rate;
                model.ItemTaxGroupTaxes.Add(m_);
            }
            return View(model);
        }

        public ActionResult TaxGroups()
        {
            var taxGroups = _administrationService.GetTaxGroups(false);
            return View();
        }
    }
}

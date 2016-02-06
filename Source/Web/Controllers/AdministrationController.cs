//-----------------------------------------------------------------------
// <copyright file="AdministrationController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Sales;
using Data;
using Services.Administration;
using Services.Inventory;
using Services.Sales;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;
using Web.Models.ViewModels.Administration;

namespace Web.Controllers
{
    [Authorize]
    public class AdministrationController : BaseController
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

                var invoice = new SalesInvoiceHeader();
                invoice.Date = invoiceDate;
                invoice.CustomerId = customer.Id;
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
                foreach (var itemTaxGroup in itemTax.ItemTaxGroupTax)
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

        public ActionResult FinancialYears()
        {
            var financialYears = _administrationService.GetFinancialYears();
            var model = new List<FinancialYearModel>();

            foreach (var year in financialYears)
            {
                model.Add(new FinancialYearModel()
                {
                    Id = year.Id,
                    FiscalYearCode = year.FiscalYearCode,
                    FiscalYearName = year.FiscalYearName,
                    StartDate = year.StartDate,
                    EndDate = year.EndDate,
                    IsActive = year.IsActive
                });
            }

            return View(model);
        }

        public ActionResult PaymentTerms()
        {
            var paymentTerms = _administrationService.GetPaymentTerms();
            var model = new HashSet<PaymentTermModel>();
            
            foreach(var term in paymentTerms)
            {
                model.Add(new PaymentTermModel()
                {
                    Id = term.Id,
                    Description = term.Description,
                    DueAfterDays = term.DueAfterDays,
                    IsActive = term.IsActive,
                    PaymentType = (int)term.PaymentType
                });
            }

            return View(model);
        }

        public ActionResult Company()
        {
            var company = _administrationService.GetDefaultCompany();
            var model = new CompanyModel()
            {
                Id = company.Id,
                ShortName = company.ShortName,
                Name = company.Name,
                CompanyCode = company.CompanyCode
            };

            return View(model);
        }

        [HttpPost]
        public ActionResult Upload()
        {
            DbInitializer<ApplicationContext> initializer = new DbInitializer<ApplicationContext>();
            if (Request.Files.Count > 0)
            {
                var file = Request.Files[0];

                if (file != null && file.ContentLength > 0)
                {
                    var fileName = System.IO.Path.GetFileName(file.FileName);
                    var path = System.IO.Path.Combine(Server.MapPath("~/App_Data/"), fileName);
                    file.SaveAs(path);

                    DbInitializerHelper.LoadChartOfAccountsFromFile(fileName, 100);
                }
            }
            return RedirectToAction("Index");
        }
    }
}

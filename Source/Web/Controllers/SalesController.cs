//-----------------------------------------------------------------------
// <copyright file="SalesController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Services.Financial;
using Services.Inventory;
using Services.Sales;
using System.Web.Mvc;
using System.Linq;
using Core.Domain.Sales;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace Web.Controllers
{
    [Authorize]
    public class SalesController : BaseController
    {
        private Web.Models.ViewModels.Sales.SalesViewModelBuilder _viewModelBuilder;

        private readonly IInventoryService _inventoryService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;

        public SalesController(IInventoryService inventoryService,
            IFinancialService financialService,
            ISalesService salesService)
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
            _salesService = salesService;

            _viewModelBuilder = new Web.Models.ViewModels.Sales.SalesViewModelBuilder(inventoryService, financialService, salesService);
        }

        public ActionResult Index()
        {
            return View();
        }

        public ActionResult SalesOrders()
        {
            var salesOrders = _salesService.GetSalesOrders();
            var model = _viewModelBuilder.CreateSalesOrdersViewModel(salesOrders.ToList());
            return View(model);
        }

        public ActionResult SalesDeliveries()
        {
            var salesDeliveries = _salesService.GetSalesDeliveries();
            var model = _viewModelBuilder.CreateSalesDeliveriesViewModel(salesDeliveries.ToList());
            return View(model);
        }

        public ActionResult AddSalesDelivery(bool direct = false)
        {
            if (direct)
            {
                var model = _viewModelBuilder.CreateSalesDeliveryViewModel();
                return View(model);
            }
            return RedirectToAction("SalesDeliveries");
        }

        [HttpPost, ActionName("AddSalesDelivery")]
        [FormValueRequiredAttribute("SaveSalesDelivery")]
        public ActionResult SaveSalesDelivery(Models.ViewModels.Sales.SalesDeliveryViewModel model)
        {
            var salesDelivery = new SalesDeliveryHeader()
            {
                CustomerId = model.CustomerId,
                PaymentTermId = model.PaymentTermId,
                Date = model.Date,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
            };
            foreach(var line in model.SalesDeliveryLines)
            {
                salesDelivery.SalesDeliveryLines.Add(new SalesDeliveryLine()
                {
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    Quantity = line.Quantity,
                    Discount = line.Discount,
                    Price = line.Quantity * line.Price,
                    CreatedBy = User.Identity.Name,
                    CreatedOn = DateTime.Now,
                    ModifiedBy = User.Identity.Name,
                    ModifiedOn = DateTime.Now,
                });
            }
            _salesService.AddSalesDelivery(salesDelivery, true);
            return RedirectToAction("SalesDeliveries");
        }

        [HttpPost, ActionName("AddSalesDelivery")]
        [FormValueRequiredAttribute("AddSalesDeliveryLineItem")]
        public ActionResult AddSalesDeliveryLineItem(Models.ViewModels.Sales.SalesDeliveryViewModel model)
        {
            var item = _inventoryService.GetItemById(model.ItemId.Value);
            var line = new Models.ViewModels.Sales.SalesDeliveryLineViewModel()
            {
                ItemId = model.ItemId,
                MeasurementId = model.MeasurementId,
                Quantity = model.Quantity,
                Price = item.Price.Value * model.Quantity,
                Discount  = model.Discount,
                LineTotalTaxAmount = item.ItemTaxAmountOutput * model.Quantity
            };

            model.SalesDeliveryLines.Add(line);
            return View(model);
        }

        [HttpPost, ActionName("AddSalesDelivery")]
        [FormValueRequiredAttribute("DeleteSaleDeliveryLineItem")]
        public ActionResult DeleteSaleDeliveryLineItem(Models.ViewModels.Sales.SalesDeliveryViewModel model)
        {
            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            model.SalesDeliveryLines.Remove(model.SalesDeliveryLines.Where(i => i.ItemId == int.Parse(deletedItem.ToString())).FirstOrDefault());
            return View(model);
        }

        public ActionResult SalesInvoices()
        {
            var invoices = _salesService.GetSalesInvoices();
            var model = new Web.Models.ViewModels.Sales.SalesInvoices();
            foreach(var invoice in invoices)
            {
                model.SalesInvoiceListLines.Add(new Models.ViewModels.Sales.SalesInvoiceListLine()
                {
                    Id = invoice.Id,
                    No = invoice.No,
                    Customer = invoice.Customer.Name,
                    Date = invoice.Date,
                    Amount = invoice.ComputeTotalAmount(),
                    IsFullPaid = invoice.IsFullPaid()
                });
            }
            return View(model);
        }

        public ActionResult AddSalesInvoice(bool direct = false)
        {
            var model = new Web.Models.ViewModels.Sales.AddSalesInvoice();
            model.Customers = Models.ModelViewHelper.Customers();
            model.Items = Models.ModelViewHelper.Items();
            model.Measurements = Models.ModelViewHelper.Measurements();

            return View(model);
        }

        [HttpPost, ActionName("AddSalesInvoice")]
        [FormValueRequiredAttribute("SaveSalesInvoice")]
        public ActionResult SaveSalesInvoice(Models.ViewModels.Sales.AddSalesInvoice model)
        {
            if (model.AddSalesInvoiceLines.Sum(i => i.Amount) == 0 || model.AddSalesInvoiceLines.Count < 1)
            {
                model.Customers = Models.ModelViewHelper.Customers();
                model.Items = Models.ModelViewHelper.Items();
                model.Measurements = Models.ModelViewHelper.Measurements();
                ModelState.AddModelError("Amount", "No invoice line");
                return View(model);
            }
            var invoiceHeader = new SalesInvoiceHeader();
            var invoiceLines = new List<SalesInvoiceLine>();
            foreach (var item in model.AddSalesInvoiceLines)
            {
                var Item = _inventoryService.GetItemById(item.ItemId);
                var invoiceDetail = new SalesInvoiceLine();
                invoiceDetail.TaxId = Item.ItemTaxGroupId;
                invoiceDetail.ItemId = item.ItemId;
                invoiceDetail.MeasurementId = item.MeasurementId;
                invoiceDetail.Quantity = item.Quantity;
                invoiceDetail.Discount = item.Discount;
                invoiceDetail.Amount = Convert.ToDecimal(item.Quantity * Item.Price);
                invoiceDetail.CreatedBy = User.Identity.Name;
                invoiceDetail.CreatedOn = DateTime.Now;
                invoiceDetail.ModifiedBy = User.Identity.Name;
                invoiceDetail.ModifiedOn = DateTime.Now;
                invoiceLines.Add(invoiceDetail);
            }
            invoiceHeader.SalesInvoiceLines = invoiceLines;
            invoiceHeader.CustomerId = model.CustomerId;
            invoiceHeader.Date = model.Date;
            invoiceHeader.ShippingHandlingCharge = 4;// model.ShippingHandlingCharge;
            invoiceHeader.CreatedBy = User.Identity.Name;
            invoiceHeader.CreatedOn = DateTime.Now;
            invoiceHeader.ModifiedBy = User.Identity.Name;
            invoiceHeader.ModifiedOn = DateTime.Now;

            _salesService.AddSalesInvoice(invoiceHeader, model.SalesOrderId);
            return RedirectToAction("SalesInvoices");
        }

        [HttpPost, ActionName("AddSalesInvoice")]
        [FormValueRequiredAttribute("AddSalesInvoiceLine")]
        public ActionResult AddSalesInvoiceLine(Models.ViewModels.Sales.AddSalesInvoice model)
        {
            model.Customers = Models.ModelViewHelper.Customers();
            model.Items = Models.ModelViewHelper.Items();
            model.Measurements = Models.ModelViewHelper.Measurements();
            if (model.Quantity > 0)
            {
                var item = _inventoryService.GetItemById(model.ItemId);
                if (!item.Price.HasValue)
                {
                    ModelState.AddModelError("Amount", "Selling price is not set.");
                    return View(model);
                }
                Models.ViewModels.Sales.AddSalesInvoiceLine itemModel = new Models.ViewModels.Sales.AddSalesInvoiceLine()
                {
                    ItemId = model.ItemId,
                    MeasurementId = model.MeasurementId,
                    Quantity = model.Quantity,
                    Discount = model.Discount,
                    Amount = item.Price.Value * model.Quantity,
                    Price = item.Price.Value,
                };
                if (model.AddSalesInvoiceLines.FirstOrDefault(i => i.ItemId == model.ItemId) == null)
                    model.AddSalesInvoiceLines.Add(itemModel);
            }
            return View(model);
        }

        [HttpPost, ActionName("AddSalesInvoice")]
        [FormValueRequiredAttribute("DeleteInvoiceLineItem")]
        public ActionResult DeleteInvoiceLineItem(Models.ViewModels.Sales.AddSalesInvoice model)
        {
            model.Customers = Models.ModelViewHelper.Customers();
            model.Items = Models.ModelViewHelper.Items();
            model.Measurements = Models.ModelViewHelper.Measurements();

            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            model.AddSalesInvoiceLines.Remove(model.AddSalesInvoiceLines.Where(i => i.ItemId == int.Parse(deletedItem.ToString())).FirstOrDefault());

            return View(model);
        }

        public ActionResult AddSalesReceipt(int? salesInvoiceId = null)
        {
            var model = new Models.ViewModels.Sales.AddSalesReceipt();
            if (salesInvoiceId.HasValue)
            {
                var salesInvoice = _salesService.GetSalesInvoiceById(salesInvoiceId.Value);
                model.SalesInvoiceId = salesInvoice.Id;
                model.SalesInvoiceNo = salesInvoice.No;
                model.InvoiceDate = salesInvoice.Date;
                model.Date = DateTime.Now;
                model.CustomerId = salesInvoice.CustomerId;

                foreach (var line in salesInvoice.SalesInvoiceLines)
                {
                    model.AddSalesReceiptLines.Add(new Models.ViewModels.Sales.AddSalesReceiptLine()
                    {
                        SalesInvoiceLineId = line.Id,
                        ItemId = line.ItemId,
                        MeasurementId = line.MeasurementId,
                        Quantity = line.Quantity,
                        Discount = line.Discount,
                        Amount = line.Amount
                    });
                }
            }
            return View(model);
        }

        [HttpPost, ActionName("AddSalesReceipt")]
        [FormValueRequiredAttribute("SaveSalesReceipt")]
        public ActionResult SaveSalesReceipt(Models.ViewModels.Sales.AddSalesReceipt model)
        {
            var receipt = new SalesReceiptHeader()
            {
                AccountToDebitId = model.AccountToDebitId,
                //SalesInvoiceHeaderId = model.SalesInvoiceId,
                CustomerId = model.CustomerId.Value,
                Date = model.Date,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
            };
            foreach(var line in model.AddSalesReceiptLines)
            {
                if(line.AmountToPay > line.Amount)
                    return RedirectToAction("SalesInvoices");

                receipt.SalesReceiptLines.Add(new SalesReceiptLine()
                {
                    SalesInvoiceLineId = line.SalesInvoiceLineId,
                    ItemId = line.ItemId,
                    MeasurementId = line.MeasurementId,
                    Quantity = line.Quantity,
                    Discount = line.Discount,
                    Amount = line.Amount,
                    AmountPaid = line.AmountToPay,
                    CreatedBy = User.Identity.Name,
                    CreatedOn = DateTime.Now,
                    ModifiedBy = User.Identity.Name,
                    ModifiedOn = DateTime.Now,
                });
            }
            _salesService.AddSalesReceipt(receipt);
            return RedirectToAction("SalesInvoices");
        }

        public ActionResult Customers()
        {
            var customers = _salesService.GetCustomers();
            var model = new Models.ViewModels.Sales.Customers();
            foreach(var customer in customers)
            {
                model.CustomerListLines.Add(new Models.ViewModels.Sales.CustomerListLine()
                {
                    Id = customer.Id,
                    Name = customer.Name,
                    //FirstName = customer.FirstName,
                    //LastName = customer.LastName,
                    Balance = customer.Balance
                });
            }
            return View(model);
        }

        public ActionResult Customer(int id = 0)
        {
            var model = new Models.ViewModels.Sales.CustomerViewModel();
            if (id == 0)
            {
                model.Id = id;
            }
            else
            {
                var customer = _salesService.GetCustomerById(id);
                var allocations = _salesService.GetCustomerReceiptsForAllocation(id);
                model.Id = customer.Id;
                model.Name = customer.Name;
                model.Balance = customer.Balance;
                foreach (var receipt in allocations)
                {
                    model.CustomerAllocations.Add(new Models.ViewModels.Sales.CustomerAllocation()
                    {
                        Id = receipt.Id,
                        AmountAllocated = receipt.SalesReceiptLines.Sum(a => a.AmountPaid),
                        AvailableAmountToAllocate = receipt.AvailableAmountToAllocate
                    });
                }
                foreach (var invoice in customer.SalesInvoices)
                {
                    model.CustomerInvoices.Add(new Models.ViewModels.Sales.CustomerSalesInvoice()
                    {
                        Id = invoice.Id,
                        InvoiceNo = invoice.No,
                        Date = invoice.Date,
                        Amount = invoice.SalesInvoiceLines.Sum(a => a.Amount),
                        Status = invoice.IsFullPaid() ? "Paid" : "Open"
                    });
                }            
            }
            return View(model);
        }

        public ActionResult AddCustomer()
        {
            var model = new Models.ViewModels.Sales.AddCustomer();
            return View(model);
        }

        [HttpPost, ActionName("AddCustomer")]
        [FormValueRequiredAttribute("SaveCustomer")]
        public ActionResult AddCustomer(Models.ViewModels.Sales.AddCustomer model)
        {
            var customer = new Customer()
            {
                Name = model.Name,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
            };
            _salesService.AddCustomer(customer);
            return RedirectToAction("EditCustomer", new { id = customer.Id });
        }

        public ActionResult AddOrEditCustomer(int id = 0)
        {
            Models.ViewModels.Sales.EditCustomer model = new Models.ViewModels.Sales.EditCustomer();
            var accounts = _financialService.GetAccounts();
            if (id != 0)
            {
                var customer = _salesService.GetCustomerById(id);
                model.Id = customer.Id;
                model.Name = customer.Name;
                model.PrimaryContactId = customer.PrimaryContactId.HasValue ? customer.PrimaryContactId.Value : -1;
                model.AccountsReceivableAccountId = customer.AccountsReceivableAccountId.HasValue ? customer.AccountsReceivableAccountId : -1;
                model.SalesAccountId = customer.SalesAccountId.HasValue ? customer.SalesAccountId : -1;
                model.SalesDiscountAccountId = customer.SalesDiscountAccountId.HasValue ? customer.SalesDiscountAccountId : -1;
                model.PromptPaymentDiscountAccountId = customer.PromptPaymentDiscountAccountId.HasValue ? customer.PromptPaymentDiscountAccountId : -1;
                model.CustomerAdvancesAccountId = customer.CustomerAdvancesAccountId.HasValue ? customer.CustomerAdvancesAccountId : -1;
            }
            return View(model);
        }

        [HttpPost, ActionName("AddOrEditCustomer")]
        [FormValueRequiredAttribute("SaveCustomer")]
        public ActionResult AddOrEditCustomer(Models.ViewModels.Sales.EditCustomer model)
        {
            Customer customer = null;
            if (model.Id != 0)
            {
                customer = _salesService.GetCustomerById(model.Id);                
            }
            else
            {
                customer = new Customer();
                customer.CreatedBy = User.Identity.Name;
                customer.CreatedOn = DateTime.Now;
            }

            customer.ModifiedBy = User.Identity.Name;
            customer.ModifiedOn = DateTime.Now;
            customer.Name = model.Name;
            if (model.PrimaryContactId != -1) customer.PrimaryContactId = model.PrimaryContactId;
            customer.AccountsReceivableAccountId = model.AccountsReceivableAccountId.Value == -1 ? null : model.AccountsReceivableAccountId;
            customer.SalesAccountId = model.SalesAccountId.Value == -1 ? null : model.SalesAccountId;
            customer.SalesDiscountAccountId = model.SalesDiscountAccountId.Value == -1 ? null : model.SalesDiscountAccountId;
            customer.PromptPaymentDiscountAccountId = model.PromptPaymentDiscountAccountId.Value == -1 ? null : model.PromptPaymentDiscountAccountId;
            customer.CustomerAdvancesAccountId = model.CustomerAdvancesAccountId.Value == -1 ? null : model.CustomerAdvancesAccountId;

            if (model.Id != 0)
                _salesService.UpdateCustomer(customer);
            else
                _salesService.AddCustomer(customer);

            return RedirectToAction("Customers");
        }

        public ActionResult CustomerDetail(int id)
        {
            var customer = _salesService.GetCustomerById(id);
            var allocations = _salesService.GetCustomerReceiptsForAllocation(id);
            var model = new Models.ViewModels.Sales.CustomerDetail()
            {
                Id = customer.Id,
                Name = customer.Name,
                Balance = customer.Balance
            };
            foreach(var receipt in allocations)
            {
                model.CustomerAllocations.Add(new Models.ViewModels.Sales.CustomerAllocation()
                {
                    Id = receipt.Id,
                    AmountAllocated = receipt.SalesReceiptLines.Sum(a => a.AmountPaid),
                    AvailableAmountToAllocate = receipt.AvailableAmountToAllocate
                });
            }
            foreach (var allocation in customer.CustomerAllocations)
            {
                model.ActualAllocations.Add(new Models.ViewModels.Sales.Allocations()
                {
                    InvoiceNo = allocation.SalesInvoiceHeader.No,
                    ReceiptNo = allocation.SalesReceiptHeader.No,
                    Date = allocation.Date,
                    Amount = allocation.Amount
                });
            }
            foreach(var invoice in customer.SalesInvoices)
            {
                model.CustomerInvoices.Add(new Models.ViewModels.Sales.CustomerSalesInvoice()
                {
                    Id = invoice.Id,
                    InvoiceNo = invoice.No,
                    Date = invoice.Date,
                    Amount = invoice.ComputeTotalAmount(),
                    Status = invoice.Status.ToString()
                 });
            }
            return View(model);
        }

        public ActionResult Allocate(int id)
        {
            var receipt = _salesService.GetSalesReceiptById(id);
            var customer = _salesService.GetCustomerById(receipt.CustomerId);
            var allocations = _salesService.GetCustomerReceiptsForAllocation(customer.Id);
            var model = new Models.ViewModels.Sales.Allocate();
            model.ReceiptId = id;
            model.TotalAmountAvailableToAllocate = allocations.Sum(a => a.AvailableAmountToAllocate);
            model.LeftToAllocateFromReceipt = receipt.AvailableAmountToAllocate;
            var openInvoices = _salesService.GetSalesInvoices().Where(inv => inv.IsFullPaid() == false);
            foreach(var invoice in openInvoices)
            {
                model.OpenInvoices.Add(new SelectListItem()
                {
                    Value = invoice.Id.ToString(),
                    Text = invoice.No + " - " + (invoice.ComputeTotalAmount() - invoice.SalesInvoiceLines.Sum(a => a.GetAmountPaid()))
                });
            }
            return PartialView(model);
        }

        [HttpPost, ActionName("Allocate")]
        [FormValueRequiredAttribute("SaveAllocation")]
        public ActionResult SaveAllocation(Models.ViewModels.Sales.Allocate model)
        {
            var receipt = _salesService.GetSalesReceiptById(model.ReceiptId);
            var customer = _salesService.GetCustomerById(receipt.CustomerId);
            var invoice = _salesService.GetSalesInvoiceById(model.InvoiceId);
            var allocations = _salesService.GetCustomerReceiptsForAllocation(customer.Id);
            model.InvoiceId = model.InvoiceId;
            model.TotalAmountAvailableToAllocate = allocations.Sum(a => a.AvailableAmountToAllocate);
            model.LeftToAllocateFromReceipt = receipt.AvailableAmountToAllocate;
            if (invoice == null)
            {
                return View(model);
            }
            else
            {
                var invoiceTotalAmount = invoice.ComputeTotalAmount();
                if (model.AmountToAllocate > invoiceTotalAmount
                    || model.AmountToAllocate > receipt.AvailableAmountToAllocate
                    || invoice.Status == Core.Domain.SalesInvoiceStatus.Closed)
                {
                    return View(model);
                }

                var allocation = new CustomerAllocation()
                {
                    CustomerId = customer.Id,
                    SalesReceiptHeaderId = receipt.Id,
                    SalesInvoiceHeaderId = invoice.Id,
                    Amount = model.AmountToAllocate,
                    Date = DateTime.Now
                };
                _salesService.SaveCustomerAllocation(allocation);
            }
            return RedirectToAction("CustomerDetail", new { id = customer.Id });
        }

        /// <summary>
        /// Add new receipt with no invoice
        /// </summary>
        /// <returns></returns>
        public ActionResult AddReceipt()
        {
            var model = new Models.ViewModels.Sales.AddSalesReceipt();
            //TODO: get the default customer advances account from GL setting if there is.
            model.AccountToCreditId = _financialService.GetAccounts().Where(a => a.AccountName == "Customer Advances").FirstOrDefault() != null
                ? _financialService.GetAccounts().Where(a => a.AccountName == "Customer Advances").FirstOrDefault().Id
                : -1;

            return View(model);
        }

        [HttpPost, ActionName("AddReceipt")]
        [FormValueRequiredAttribute("SaveReceipt")]
        public ActionResult SaveReceipt(Models.ViewModels.Sales.AddSalesReceipt model)
        {
            if (model.AccountToDebitId.Value == -1 || model.CustomerId.Value == -1)
                return View(model);

            var receipt = new SalesReceiptHeader()
            {
                AccountToDebitId = model.AccountToDebitId,
                CustomerId = model.CustomerId.Value,
                Date = model.Date,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
                Amount = model.PaymentAmount
            };

            receipt.SalesReceiptLines.Add(new SalesReceiptLine()
            {
                AccountToCreditId = model.AccountToCreditId,
                Amount = model.Amount,
                AmountPaid = model.PaymentAmount,
                CreatedBy = User.Identity.Name,
                CreatedOn = DateTime.Now,
                ModifiedBy = User.Identity.Name,
                ModifiedOn = DateTime.Now,
            });

            _salesService.AddSalesReceiptNoInvoice(receipt);
            return RedirectToAction("Receipts");
        }

        [HttpPost, ActionName("AddReceipt")]
        [FormValueRequiredAttribute("AddReceiptItem")]
        public ActionResult AddReceiptItem(Models.ViewModels.Sales.AddSalesReceipt model)
        {
            var rowId = Guid.NewGuid();
            if (model.ItemId.Value != -1 && model.Quantity > 0)
            {
                var item = _inventoryService.GetItemById(model.ItemId.Value);
                if (!item.Price.HasValue)
                {
                    ModelState.AddModelError("Amount", "Selling price is not set.");
                    return View(model);
                }
                Models.ViewModels.Sales.AddSalesReceiptLine itemModel = new Models.ViewModels.Sales.AddSalesReceiptLine()
                {
                    RowId = rowId.ToString(),
                    ItemId = model.ItemId.Value,
                    MeasurementId = model.MeasurementId.Value,
                    Quantity = model.Quantity,
                    Discount = model.Discount,
                    Amount = item.Price.Value * model.Quantity,
                    AmountToPay = model.AmountToPay
                };
                if (model.AddSalesReceiptLines.FirstOrDefault(i => i.ItemId == model.ItemId) == null)
                    model.AddSalesReceiptLines.Add(itemModel);
            }
            else if(!string.IsNullOrEmpty(model.AccountCode) && model.Amount != 0)
            {
                var account = _financialService.GetAccounts().Where(a => a.AccountCode == model.AccountCode).FirstOrDefault();
                if(account != null)
                {
                    Models.ViewModels.Sales.AddSalesReceiptLine accountItemModel = new Models.ViewModels.Sales.AddSalesReceiptLine()
                    {
                        RowId = rowId.ToString(),
                        AccountToCreditId = account.Id,
                        Amount = model.AmountToPay,
                        AmountToPay = model.AmountToPay,
                    };
                    model.AddSalesReceiptLines.Add(accountItemModel);
                }                
            }
            return View(model);
        }

        [HttpPost, ActionName("AddReceipt")]
        [FormValueRequiredAttribute("DeleteReceiptLineItem")]
        public ActionResult DeleteReceiptLineItem(Models.ViewModels.Sales.AddSalesReceipt model)
        {
            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            model.AddSalesReceiptLines.Remove(model.AddSalesReceiptLines.Where(i => i.ItemId.ToString() == deletedItem.ToString()).FirstOrDefault());
            return View(model);
        }

        public ActionResult Receipts()
        {
            var receipts = _salesService.GetSalesReceipts();
            var model = new Models.ViewModels.Sales.SalesReceipts();
            foreach(var receipt in receipts)
            {
                model.SalesReceiptListLines.Add(new Models.ViewModels.Sales.SalesReceiptListLine()
                {
                    No = receipt.No,
                    //InvoiceNo = receipt.SalesInvoiceHeader != null ? receipt.SalesInvoiceHeader.No : string.Empty,
                    CustomerId = receipt.CustomerId,
                    CustomerName = receipt.Customer.Name,
                    Date = receipt.Date,
                    Amount = receipt.SalesReceiptLines.Sum(r => r.Amount),
                    AmountPaid = receipt.SalesReceiptLines.Sum(r => r.AmountPaid)
                });
            }
            return View(model);
        }

        public ActionResult SalesDelivery(int id = 0)
        {
            var model = new Web.Models.ViewModels.Sales.SalesHeaderViewModel(_inventoryService, _financialService);
            model.DocumentType = Core.Domain.DocumentTypes.SalesDelivery;

            if (id == 0)
            {
                return View(model);
            }
            else
            {
                var invoice = _salesService.GetSalesInvoiceById(id);
                model.Id = invoice.Id;
                model.CustomerId = invoice.CustomerId;
                model.Date = invoice.Date;
                model.No = invoice.No;
                foreach (var line in invoice.SalesInvoiceLines)
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
                return View(model);
            }
        }

        public ActionResult SalesInvoice(int id = 0)
        {
            var model = new Web.Models.ViewModels.Sales.SalesHeaderViewModel(_inventoryService, _financialService);
            model.DocumentType = Core.Domain.DocumentTypes.SalesInvoice;

            if (id == 0)
            {   
                return View(model);
            }
            else
            {
                var invoice = _salesService.GetSalesInvoiceById(id);
                model.Id = invoice.Id;
                model.CustomerId = invoice.CustomerId;
                model.Date = invoice.Date;
                model.No = invoice.No;
                foreach (var line in invoice.SalesInvoiceLines)
                {
                    var lineItem = new Models.ViewModels.Sales.SalesLineItemViewModel(_financialService);
                    lineItem.SetServiceHelpers(_financialService);
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
                return View(model);
            }
        }

        public ActionResult SalesOrder(int id = 0)
        {
            var model = new Models.ViewModels.Sales.SalesHeaderViewModel(_inventoryService, _financialService);
            model.DocumentType = Core.Domain.DocumentTypes.SalesOrder;

            if (id == 0)
            {
                return View(model);
            }
            else
            {
                var order = _salesService.GetSalesOrderById(id);
                model.Id = order.Id;
                model.CustomerId = order.CustomerId;
                model.PaymentTermId = order.PaymentTermId;
                model.Date = order.Date;
                model.Reference = order.ReferenceNo;
                model.No = order.No;

                foreach (var line in order.SalesOrderLines)
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
                return View(model);
            }
        }

        [HttpPost, ActionName("SalesOrder")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveOrder(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            SalesOrderHeader order = null;
            if (model.Id == 0)
            {
                order = new SalesOrderHeader();
                order.CreatedBy = User.Identity.Name;
                order.CreatedOn = DateTime.Now;
            }
            else
            {
                order = _salesService.GetSalesOrderById(model.Id);
            }

            order.ModifiedBy = User.Identity.Name;
            order.ModifiedOn = DateTime.Now;
            order.CustomerId = model.CustomerId.Value;
            order.PaymentTermId = model.PaymentTermId;
            order.Date = model.Date;

            foreach (var line in model.SalesLine.SalesLineItems)
            {
                SalesOrderLine lineItem = null;
                var item = _inventoryService.GetItemByNo(line.ItemNo);
                if (!line.Id.HasValue)
                {
                    lineItem = new SalesOrderLine();
                    lineItem.CreatedBy = User.Identity.Name;
                    lineItem.CreatedOn = DateTime.Now;
                    order.SalesOrderLines.Add(lineItem);
                }
                else
                {
                    lineItem = order.SalesOrderLines.Where(i => i.Id == line.Id).FirstOrDefault();
                }

                lineItem.ModifiedBy = User.Identity.Name;
                lineItem.ModifiedOn = DateTime.Now;
                lineItem.ItemId = line.ItemId;
                lineItem.MeasurementId = item.SellMeasurementId.Value;
                lineItem.Quantity = line.Quantity;
                lineItem.Discount = line.Discount;
                lineItem.Amount = line.Price;
            }

            if (model.Id == 0)
                _salesService.AddSalesOrder(order, true);
            else
                _salesService.UpdateSalesOrder(order);

            return RedirectToAction("SalesOrders");
        }

        [HttpPost, ActionName("SalesDelivery")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveDelivery(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            SalesDeliveryHeader delivery = null;
            if (model.Id == 0)
            {
                delivery = new SalesDeliveryHeader();
                delivery.CreatedBy = User.Identity.Name;
                delivery.CreatedOn = DateTime.Now;
            }
            else
            {
                delivery = _salesService.GetSalesDeliveryById(model.Id);
            }

            delivery.ModifiedBy = User.Identity.Name;
            delivery.ModifiedOn = DateTime.Now;
            delivery.CustomerId = model.CustomerId.Value;
            delivery.PaymentTermId = model.PaymentTermId;
            delivery.Date = model.Date;

            foreach (var line in model.SalesLine.SalesLineItems)
            {
                SalesDeliveryLine lineItem = null;
                var item = _inventoryService.GetItemByNo(line.ItemNo);
                if (!line.Id.HasValue)
                {
                    lineItem = new SalesDeliveryLine();
                    lineItem.CreatedBy = User.Identity.Name;
                    lineItem.CreatedOn = DateTime.Now;
                    delivery.SalesDeliveryLines.Add(lineItem);
                }
                else
                {
                    lineItem = delivery.SalesDeliveryLines.Where(i => i.Id == line.Id).FirstOrDefault();
                }

                lineItem.ModifiedBy = User.Identity.Name;
                lineItem.ModifiedOn = DateTime.Now;
                lineItem.ItemId = line.ItemId;
                lineItem.MeasurementId = item.SellMeasurementId.Value;
                lineItem.Quantity = line.Quantity;
                lineItem.Discount = line.Discount;
                lineItem.Price = line.Price;
            }

            if (model.Id == 0)
            {
                _salesService.AddSalesDelivery(delivery, true);
            }
            else
            {
                
            }

            return RedirectToAction("SalesDeliveries");
        }


        [HttpPost, ActionName("SalesInvoice")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveInvoice(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            SalesInvoiceHeader invoice = null;
            if (model.Id == 0)
            {
                invoice = new SalesInvoiceHeader();
                invoice.CreatedBy = User.Identity.Name;
                invoice.CreatedOn = DateTime.Now;
            }
            else
            {
                invoice = _salesService.GetSalesInvoiceById(model.Id);
            }

            invoice.ModifiedBy = User.Identity.Name;
            invoice.ModifiedOn = DateTime.Now;
            invoice.CustomerId = model.CustomerId.Value;
            invoice.Date = model.Date;
            invoice.ShippingHandlingCharge = model.ShippingHandlingCharges;
            
            foreach (var line in model.SalesLine.SalesLineItems)
            {
                SalesInvoiceLine lineItem = null;
                var item = _inventoryService.GetItemByNo(line.ItemNo);
                if (!line.Id.HasValue)
                {
                    lineItem = new SalesInvoiceLine();
                    lineItem.CreatedBy = User.Identity.Name;
                    lineItem.CreatedOn = DateTime.Now;
                    invoice.SalesInvoiceLines.Add(lineItem);
                }
                else
                {
                    lineItem = invoice.SalesInvoiceLines.Where(i => i.Id == line.Id).FirstOrDefault();
                }

                lineItem.ModifiedBy = User.Identity.Name;
                lineItem.ModifiedOn = DateTime.Now;
                lineItem.ItemId = line.ItemId;
                lineItem.MeasurementId = item.SellMeasurementId.Value;
                lineItem.Quantity = line.Quantity;
                lineItem.Discount = line.Discount;
                lineItem.Amount = line.Price;
            }

            if (model.Id == 0)
            {
                _salesService.AddSalesInvoice(invoice, null);
            }
            else
            {
                _salesService.UpdateSalesInvoice(invoice);
            }

            return RedirectToAction("SalesInvoices");
        }

        [HttpPost, ActionName("SalesOrder")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemOrder(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("SalesInvoice")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemInvoice(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("SalesDelivery")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemDelivery(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        public ActionResult ReturnView(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            string actionName = string.Empty;
            switch (model.DocumentType)
            {
                case Core.Domain.DocumentTypes.SalesOrder:
                    actionName = "SalesOrder";
                    break;
                case Core.Domain.DocumentTypes.SalesInvoice:
                    actionName = "SalesInvoice";
                    break;
                case Core.Domain.DocumentTypes.SalesDelivery:
                    actionName = "SalesDelivery";
                    break;
            }

            return View(actionName, model);
        }

        private void AddLineItem(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            if (!model.CustomerId.HasValue)
                throw new Exception("Please enter customer.");

            var item = _inventoryService.GetItemByNo(model.SalesLine.ItemNo);
            var newLine = new Models.ViewModels.Sales.SalesLineItemViewModel(_financialService);
            newLine.CustomerId = model.CustomerId.HasValue ? model.CustomerId.Value : 0;
            newLine.ItemId = item.Id;
            newLine.ItemNo = item.No;
            newLine.ItemDescription = item.Description;
            newLine.Measurement = item.SellMeasurement.Description;
            newLine.Quantity = model.SalesLine.Quantity;
            newLine.Price = model.SalesLine.Price;
            newLine.Discount = model.SalesLine.Discount;            
            model.SalesLine.SalesLineItems.Add(newLine);

            foreach (var line in model.SalesLine.SalesLineItems)
                line.SetServiceHelpers(_financialService);
        }

        [HttpPost, ActionName("SalesOrder")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemOrder(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("SalesInvoice")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemInvoice(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("SalesDelivery")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemDelivery(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        private void DeleteLineItem(Models.ViewModels.Sales.SalesHeaderViewModel model)
        {
            model.SetServiceHelpers(_inventoryService, _financialService);
            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            if (!string.IsNullOrEmpty(deletedItem))
            {
                model.SalesLine.SalesLineItems.RemoveAt(int.Parse(deletedItem));
            }
        }

        public ActionResult AddCustomerContact()
        {
            var model = new Web.Models.ViewModels.ContactViewModel();
            return View("Contact", model);
        }

        [HttpPost]
        public ActionResult AddCustomerContact(Web.Models.ViewModels.ContactViewModel model)
        {
            var contact = new Core.Domain.Contact();
            contact.ContactType = Core.Domain.ContactTypes.Customer;
            contact.FirstName = model.FirstName;
            contact.MiddleName = model.MiddleName;
            contact.LastName = model.LastName;
            contact.IsActive = true;
            contact.CreatedBy = User.Identity.Name;
            contact.CreatedOn = DateTime.Now;
            contact.ModifiedBy = User.Identity.Name;
            contact.ModifiedOn = DateTime.Now;
            _salesService.SaveContact(contact);
            if(contact.Id > 0)
                return Json(new { Status = "success" });
            else
                return Json(new { Status = "falied" });
        }
    }
}

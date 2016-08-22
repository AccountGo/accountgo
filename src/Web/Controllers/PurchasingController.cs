//-----------------------------------------------------------------------
// <copyright file="PurchasingController.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:48:38 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain.Purchases;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.TaxSystem;
using System;
using System.Linq;
using System.Web.Mvc;

namespace Web.Controllers
{
    [Authorize]
    public class PurchasingController : BaseController
    {
        private readonly IInventoryService _inventoryService;
        private readonly IFinancialService _financialService;
        private readonly IPurchasingService _purchasingService;
        private readonly ITaxService _taxService;

        public PurchasingController(IInventoryService inventoryService,
            IFinancialService financialService,
            IPurchasingService purchasingService,
            ITaxService taxService)
        {
            _inventoryService = inventoryService;
            _financialService = financialService;
            _purchasingService = purchasingService;
            _taxService = taxService;
        }

        public ActionResult PurchaseOrders()
        {
            var purchaseOrders = _purchasingService.GetPurchaseOrders();
            var model = new Models.ViewModels.Purchases.PurchaseOrders();
            foreach (var po in purchaseOrders)
            {
                model.PurchaseOrderListLines.Add(new Models.ViewModels.Purchases.PurchaseOrderListLine()
                {
                    Id = po.Id,
                    No = po.No,
                    Date = po.Date,
                    Vendor = po.Vendor.No,
                    Amount = po.PurchaseOrderLines.Sum(e => e.Amount),
                    //Completed = po.IsCompleted(),
                    //Paid = po.IsPaid(),
                    //HasInvoiced = po.PurchaseInvoiceHeaderId.HasValue
                });
            }
            return View(model);
        }

        public ActionResult AddPurchaseOrder()
        {
            var model = new Models.ViewModels.Purchases.AddPurchaseOrder();
            var items = _inventoryService.GetAllItems();
            var accounts = _financialService.GetAccounts();
            var measurements = _inventoryService.GetMeasurements();
            var taxes = _financialService.GetTaxes();
            var itemCategories = _inventoryService.GetItemCategories();
            var vendors = _purchasingService.GetVendors();
            model.Items = Models.ModelViewHelper.Items();
            model.Vendors = Models.ModelViewHelper.Vendors();
            model.UnitOfMeasurements = Models.ModelViewHelper.Measurements();
            return View(model);
        }

        [HttpPost, ActionName("AddPurchaseOrder")]
        [FormValueRequiredAttribute("AddPurchaseOrderLine")]
        public ActionResult AddPurchaseOrderLine(Models.ViewModels.Purchases.AddPurchaseOrder model)
        {
            var items = _inventoryService.GetAllItems();
            var accounts = _financialService.GetAccounts();
            var measurements = _inventoryService.GetMeasurements();
            var taxes = _financialService.GetTaxes();
            var itemCategories = _inventoryService.GetItemCategories();
            var vendors = _purchasingService.GetVendors();
            model.Items = Models.ModelViewHelper.Items();
            model.Vendors = Models.ModelViewHelper.Vendors();
            model.UnitOfMeasurements = Models.ModelViewHelper.Measurements();
            try
            {
                if (model.Quantity > 0)
                {
                    var item = _inventoryService.GetItemById(model.ItemId);
                    model.PurchaseOrderLines.Add(new Models.ViewModels.Purchases.AddPurchaseOrderLine()
                    {
                        ItemId = model.ItemId,
                        Description = item.PurchaseDescription,
                        Cost = item.Cost,
                        UnitOfMeasurementId = model.UnitOfMeasurementId,
                        Quantity = model.Quantity,
                        TotalLineCost = item.Cost.Value * model.Quantity
                    });
                }

                return View(model);
            }
            catch
            {
                return View(model);
            }
        }

        [HttpPost, ActionName("AddPurchaseOrder")]
        [FormValueRequiredAttribute("SavePurchaseOrder")]
        public ActionResult SavePurchaseOrder(Models.ViewModels.Purchases.AddPurchaseOrder model)
        {
            var items = _inventoryService.GetAllItems();
            var accounts = _financialService.GetAccounts();
            var measurements = _inventoryService.GetMeasurements();
            var taxes = _financialService.GetTaxes();
            var itemCategories = _inventoryService.GetItemCategories();
            var vendors = _purchasingService.GetVendors();
            model.Items = Models.ModelViewHelper.Items();
            model.Vendors = Models.ModelViewHelper.Vendors();
            model.UnitOfMeasurements = Models.ModelViewHelper.Measurements();
            try
            {
                var po = new PurchaseOrderHeader()
                {
                    VendorId = model.VendorId,
                    Date = model.Date,
                    //No = _settingService.GetNextNumber(Core.Module.Common.Data.SequenceNumberTypes.PurchaseOrder).ToString(),
                    //DocumentTypeId = (int)DocumentTypes.PurchaseOrder
                };
                foreach (var item in model.PurchaseOrderLines)
                {
                    var persistedItem = _inventoryService.GetItemById(item.ItemId);
                    po.PurchaseOrderLines.Add(new PurchaseOrderLine()
                    {
                        Amount = item.TotalLineCost,
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        MeasurementId = item.UnitOfMeasurementId,
                        Cost = persistedItem.Cost.Value,
                    });
                }
                _purchasingService.AddPurchaseOrder(po, true);
                return RedirectToAction("PurchaseOrders");
            }
            catch
            {
                return View(model);
            }
        }

        [HttpPost, ActionName("AddPurchaseOrder")]
        [FormValueRequiredAttribute("DeletePurchaseOrderLine")]
        public ActionResult DeletePurchaseOrderLine(Web.Models.ViewModels.Purchases.AddPurchaseOrder model)
        {
            var items = _inventoryService.GetAllItems();
            var accounts = _financialService.GetAccounts();
            var measurements = _inventoryService.GetMeasurements();
            var taxes = _financialService.GetTaxes();
            var itemCategories = _inventoryService.GetItemCategories();
            var vendors = _purchasingService.GetVendors();
            model.Items = Models.ModelViewHelper.Items();
            model.Vendors = Models.ModelViewHelper.Vendors();
            model.UnitOfMeasurements = Models.ModelViewHelper.Measurements();

            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            model.PurchaseOrderLines.Remove(model.PurchaseOrderLines.Where(i => i.ItemId == int.Parse(deletedItem.ToString())).FirstOrDefault());
            return View(model);
        }

        public ActionResult AddPurchaseReceipt(int id)
        {
            var po = _purchasingService.GetPurchaseOrderById(id);
            var model = new Models.ViewModels.Purchases.AddPurchaseReceipt();
            model.PreparePurchaseReceiptViewModel(po);
            return View(model);
        }

        [HttpPost, ActionName("AddPurchaseReceipt")]
        [FormValueRequiredAttribute("SavePurchaseReceipt")]
        public ActionResult AddPurchaseReceipt(Models.ViewModels.Purchases.AddPurchaseReceipt model)
        {
            bool hasChanged = false;
            foreach (var line in model.PurchaseReceiptLines)
            {
                if (line.InQty.HasValue && line.InQty.Value != 0)
                {
                    hasChanged = true;
                    break;
                }
            }

            if (!hasChanged)
                return RedirectToAction("PurchaseOrders");

            var po = _purchasingService.GetPurchaseOrderById(model.Id);

            var poReceipt = new PurchaseReceiptHeader()
            {
                Date = DateTime.Now,
                Vendor = po.Vendor,
                VendorId = po.VendorId.Value,
                //PurchaseOrderHeaderId = po.Id,
            };

            foreach (var receipt in model.PurchaseReceiptLines)
            {
                if((receipt.InQty + receipt.ReceiptQuantity) > receipt.Quantity)
                    return RedirectToAction("PurchaseOrders");

                poReceipt.PurchaseReceiptLines.Add(new PurchaseReceiptLine()
                {
                    //PurchaseOrderLineId = receipt.PurchaseOrderLineId,
                    ItemId = receipt.ItemId,
                    MeasurementId = receipt.UnitOfMeasurementId,
                    Quantity = receipt.Quantity,
                    ReceivedQuantity = (receipt.InQty.HasValue ? receipt.InQty.Value : 0),
                    Cost = receipt.Cost.Value,
                    Amount = receipt.Cost.Value * (receipt.InQty.HasValue ? receipt.InQty.Value : 0),
                });
            }

            _purchasingService.AddPurchaseOrderReceipt(poReceipt);
            return RedirectToAction("PurchaseOrders");
        }

        public ActionResult AddPurchaseInvoice(int? id = null)
        {
            Models.ViewModels.Purchases.AddPurchaseInvoice model = new Models.ViewModels.Purchases.AddPurchaseInvoice();
            if (id != null)
            {
                var existingPO = _purchasingService.GetPurchaseOrderById(id.Value);
                model.Date = existingPO.Date;
                model.Vendor = existingPO.Vendor.No;
                model.No = existingPO.No;
                model.Amount = existingPO.PurchaseOrderLines.Sum(a => a.Amount);

                foreach (var line in existingPO.PurchaseOrderLines)
                {
                    model.PurchaseInvoiceLines.Add(new Models.ViewModels.Purchases.AddPurchaseInvoiceLine()
                    {
                        Id = line.Id,
                        ItemId = line.ItemId,
                        UnitOfMeasurementId = line.MeasurementId,
                        Description = line.Item.Description,
                        Quantity = line.Quantity,
                        Cost = line.Cost,
                        TotalLineCost = line.Cost * line.Quantity,
                        //ReceivedQuantity = line.GetReceivedQuantity().Value
                    });
                }
            }
            return View(model);
        }

        [HttpPost, ActionName("AddPurchaseInvoice")]
        [FormValueRequiredAttribute("SavePurchaseInvoice")]
        public ActionResult AddPurchaseInvoice(Models.ViewModels.Purchases.AddPurchaseInvoice model)
        {
            if(string.IsNullOrEmpty(model.VendorInvoiceNo))
                return RedirectToAction("PurchaseOrders");

            var existingPO = _purchasingService.GetPurchaseOrderById(model.Id);
            var vendor = _purchasingService.GetVendorById(existingPO.VendorId.Value);

            var purchInvoice = new PurchaseInvoiceHeader()
            {
                Date = model.Date,
                VendorInvoiceNo = model.VendorInvoiceNo,
                Vendor = vendor,
                VendorId = vendor.Id,
            };

            foreach (var line in model.PurchaseInvoiceLines)
            {
                var item = _inventoryService.GetItemById(line.ItemId);
                var measurement = _inventoryService.GetMeasurementById(line.UnitOfMeasurementId);
                purchInvoice.PurchaseInvoiceLines.Add(new PurchaseInvoiceLine()
                {
                    ItemId = item.Id,
                    MeasurementId = measurement.Id,
                    Quantity = line.Quantity,
                    ReceivedQuantity = line.ReceivedQuantity,
                    Cost = item.Cost.Value,
                    Discount = 0,
                    Amount = item.Cost.Value * line.ReceivedQuantity,
                });
            }
            _purchasingService.AddPurchaseInvoice(purchInvoice, existingPO.Id);
            return RedirectToAction("PurchaseOrders");
        }

        public ActionResult Vendors()
        {
            var vendors = _purchasingService.GetVendors();
            var model = new Web.Models.ViewModels.Purchases.Vendors();
            if (vendors != null)
            {
                foreach (var vendor in vendors)
                {
                    model.VendorsList.Add(new Models.ViewModels.Purchases.VendorsListLine()
                    {
                        Id = vendor.Id,
                        Name = vendor.Party.Name == null ? "Name" : vendor.Party.Name,
                        Balance = vendor.GetBalance()
                    });
                }
            }
            return View(model);
        }

        public ActionResult AddOrEditVendor(int id = 0)
        {
            Vendor vendor = null;
            var model = new Web.Models.ViewModels.Purchases.Vendor();
            model.Id = id;
            if (id != 0)
            {
                vendor = _purchasingService.GetVendorById(id);                
                model.VendorName = vendor.Party.Name;
                model.AccountsPayableAccountId = vendor.AccountsPayableAccountId;
                model.PurchaseAccountId = vendor.PurchaseAccountId;
                model.PurchaseDiscountAccountId = vendor.PurchaseDiscountAccountId;
            }

            return View(model);
        }

        [HttpPost, ActionName("AddOrEditVendor")]
        [FormValueRequiredAttribute("SaveVendor")]
        public ActionResult AddOrEditVendor(Web.Models.ViewModels.Purchases.Vendor model)
        {
            Vendor vendor = null;
            if (model.Id != 0)
            {
                vendor = _purchasingService.GetVendorById(model.Id.Value);
            }
            else
            {
                vendor = new Vendor();
            }
            
            vendor.Party.Name = model.VendorName;
            vendor.AccountsPayableAccountId = model.AccountsPayableAccountId.Value == -1 ? null : model.AccountsPayableAccountId;
            vendor.PurchaseAccountId = model.PurchaseAccountId.Value == -1 ? null : model.PurchaseAccountId;
            vendor.PurchaseDiscountAccountId = model.PurchaseDiscountAccountId.Value == -1 ? null : model.PurchaseDiscountAccountId;

            if (model.Id != 0)
                _purchasingService.UpdateVendor(vendor);
            else
                _purchasingService.AddVendor(vendor);

            return RedirectToAction("Vendors");
        }

        public ActionResult AddVendor()
        {
            var model = new Models.ViewModels.Purchases.AddVendor();
            model.Accounts = Models.ModelViewHelper.Accounts();
            return View(model);
        }

        [HttpPost, ActionName("AddVendor")]
        [FormValueRequiredAttribute("SaveVendor")]
        public ActionResult AddVendor(Models.ViewModels.Purchases.AddVendor model)
        {
            var vendor = new Vendor()
            {
                AccountsPayableAccountId = model.AccountsPayableAccountId.Value == -1 ? null : model.AccountsPayableAccountId,
                PurchaseAccountId = model.PurchaseAccountId.Value == -1 ? null : model.PurchaseAccountId,
                PurchaseDiscountAccountId = model.PurchaseDiscountAccountId.Value == -1 ? null : model.PurchaseDiscountAccountId,
            };
            vendor.Party.Name = model.VendorName;

            _purchasingService.AddVendor(vendor);

            return RedirectToAction("Vendors");
        }

        public ActionResult PurchaseInvoices()
        {
            var invoices = _purchasingService.GetPurchaseInvoices();
            var model = new Models.ViewModels.Purchases.PurchaseInvoices();
                        
            foreach(var invoice in invoices)
            {
                var invoiceModel = new Models.ViewModels.Purchases.PurchaseInvoiceListLine()
                {
                    Id = invoice.Id,
                    No = invoice.No,
                    Date = invoice.Date,
                    Vendor = invoice.Vendor.Party.Name,
                    TotalAmount = invoice.PurchaseInvoiceLines.Sum(a => a.Amount),
                    IsPaid = invoice.IsPaid(),
                    TotalTax = _taxService.GetPurchaseTaxes(invoice.VendorId.Value, invoice.PurchaseInvoiceLines.AsEnumerable()).Sum(t => t.Value)
                };

                model.PurchaseInvoiceListLines.Add(invoiceModel);
            }
            return View(model);
        }

        public ActionResult MakePayment(int id)
        {
            var model = new Models.ViewModels.Purchases.MakePayment();
            var invoice = _purchasingService.GetPurchaseInvoiceById(id);

            model.InvoiceId = invoice.Id;
            model.InvoiceNo = invoice.No;
            model.Vendor = invoice.Vendor.Party.Name;
            model.Amount = invoice.GeneralLedgerHeader.GeneralLedgerLines.Where(dr => dr.DrCr == Core.Domain.DrOrCrSide.Dr).Sum(l => l.Amount);

            return View(model);
        }

        [HttpPost, ActionName("MakePayment")]
        [FormValueRequiredAttribute("SavePaymentToVendor")]
        public ActionResult SavePaymentToVendor(Models.ViewModels.Purchases.MakePayment model)
        {
            var invoice = _purchasingService.GetPurchaseInvoiceById(model.InvoiceId);
            if(model.AmountToPay < 1)
                return RedirectToAction("MakePayment", new { id = model.InvoiceId });
            _purchasingService.SavePayment(invoice.Id, invoice.VendorId.Value, model.AccountId, model.AmountToPay, DateTime.Now);
            return RedirectToAction("PurchaseInvoices");
        }

        public ActionResult PurchaseOrder(int id = 0)
        {
            var model = new Models.ViewModels.Purchases.PurchaseHeaderViewModel();
            model.DocumentType = Core.Domain.DocumentTypes.PurchaseOrder;
            if (id == 0)
            {
                model.Id = id;
                return View(model);
            }
            else
            {
                var order = _purchasingService.GetPurchaseOrderById(id);
                model.Id = order.Id;
                model.VendorId = order.VendorId;
                model.Date = order.Date;
                model.ReferenceNo = string.Empty;
                foreach (var line in order.PurchaseOrderLines)
                {
                    var lineItem = new Models.ViewModels.Purchases.PurchaseLineItemViewModel();
                    lineItem.Id = line.Id;
                    lineItem.ItemId = line.ItemId;
                    lineItem.ItemNo = line.Item.No;
                    lineItem.ItemDescription = line.Item.Description;
                    lineItem.Measurement = line.Measurement.Description;
                    lineItem.Quantity = line.Quantity;
                    lineItem.Price = line.Amount;
                    model.PurchaseLine.PurchaseLineItems.Add(lineItem);
                }
                return View(model);
            }
        }

        public ActionResult PurchaseDelivery(int id = 0, int orderid = 0)
        {
            var model = new Models.ViewModels.Purchases.PurchaseHeaderViewModel();
            model.DocumentType = Core.Domain.DocumentTypes.PurchaseReceipt;
            if (id == 0)
            {
                model.Id = id;
                if (orderid != 0)
                {
                    var order = _purchasingService.GetPurchaseOrderById(orderid);
                    model.Id = order.Id;
                    model.VendorId = order.VendorId;
                    model.Date = order.Date;
                    model.ReferenceNo = string.Empty;
                    foreach (var line in order.PurchaseOrderLines)
                    {
                        var lineItem = new Models.ViewModels.Purchases.PurchaseLineItemViewModel();
                        lineItem.Id = line.Id;
                        lineItem.ItemId = line.ItemId;
                        lineItem.ItemNo = line.Item.No;
                        lineItem.ItemDescription = line.Item.Description;
                        lineItem.Measurement = line.Measurement.Description;
                        lineItem.Quantity = line.Quantity;
                        lineItem.Price = line.Amount;
                        model.PurchaseLine.PurchaseLineItems.Add(lineItem);
                    }
                }
                return View(model);
            }
            else
            {
                var delivery = _purchasingService.GetPurchaseReceiptById(id);
                model.Id = delivery.Id;
                model.VendorId = delivery.VendorId;
                model.Date = delivery.Date;
                model.ReferenceNo = string.Empty;
                foreach (var line in delivery.PurchaseReceiptLines)
                {
                    var lineItem = new Models.ViewModels.Purchases.PurchaseLineItemViewModel();
                    lineItem.Id = line.Id;
                    lineItem.ItemId = line.ItemId;
                    lineItem.ItemNo = line.Item.No;
                    lineItem.ItemDescription = line.Item.Description;
                    lineItem.Measurement = line.Measurement.Description;
                    lineItem.Quantity = line.Quantity;
                    lineItem.Price = line.Amount;
                    model.PurchaseLine.PurchaseLineItems.Add(lineItem);
                }
                return View(model);
            }
        }

        public ActionResult PurchaseInvoice(int id = 0, int deliveryId = 0)
        {
            var model = new Models.ViewModels.Purchases.PurchaseHeaderViewModel();
            model.DocumentType = Core.Domain.DocumentTypes.PurchaseInvoice;
            model.IsDirect = deliveryId == 0;
            model.Id = id;
            if (id == 0)
            {
                return View(model);
            }
            else
            {
                var invoice = _purchasingService.GetPurchaseInvoiceById(id);
                model.Id = invoice.Id;
                model.VendorId = invoice.VendorId;
                model.Date = invoice.Date;
                model.ReferenceNo = string.Empty;
                foreach (var line in invoice.PurchaseInvoiceLines)
                {
                    var lineItem = new Models.ViewModels.Purchases.PurchaseLineItemViewModel();
                    lineItem.Id = line.Id;
                    lineItem.ItemId = line.ItemId;
                    lineItem.ItemNo = line.Item.No;
                    lineItem.ItemDescription = line.Item.Description;
                    lineItem.Measurement = line.Measurement.Description;
                    lineItem.Quantity = line.Quantity;
                    lineItem.Price = line.Amount;
                    model.PurchaseLine.PurchaseLineItems.Add(lineItem);
                }
                return View(model);
            }
        }

        [NonAction]
        protected void AddLineItem(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            var item = _inventoryService.GetItemByNo(model.PurchaseLine.ItemNo);
            var newLine = new Models.ViewModels.Purchases.PurchaseLineItemViewModel()
            {
                ItemId = item.Id,
                ItemNo = item.No,
                ItemDescription = item.Description,
                Measurement = item.SellMeasurement.Description,
                Quantity = model.PurchaseLine.Quantity,
                Price = model.PurchaseLine.Price,
            };
            model.PurchaseLine.PurchaseLineItems.Add(newLine);

            foreach (var line in model.PurchaseLine.PurchaseLineItems)
            {
                var taxes = _financialService.ComputeInputTax(model.VendorId.Value, line.ItemId, line.Quantity, line.Price, decimal.Zero);
                var taxVM = new Models.ViewModels.Purchases.PurchaseLineItemTaxViewModel();
                foreach (var tax in taxes)
                {
                    var t = _financialService.GetTaxes().Where(tx => tx.Id == int.Parse(tax.Key.ToString())).FirstOrDefault();
                    taxVM.TaxId = int.Parse(tax.Key.ToString());
                    taxVM.Amount = tax.Value;
                    taxVM.TaxRate = t.Rate;
                    taxVM.TaxName = t.TaxName;
                    model.PurchaseLine.PurchaseLineItemsTaxes.Add(taxVM);
                }
            }
        }

        public ActionResult ReturnView(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            string actionName = string.Empty;
            switch (model.DocumentType)
            {
                case Core.Domain.DocumentTypes.SalesOrder:
                    actionName = "PurchaseOrder";
                    break;
                case Core.Domain.DocumentTypes.SalesInvoice:
                    actionName = "Purchasevoice";
                    break;
                case Core.Domain.DocumentTypes.SalesDelivery:
                    actionName = "PurchaseDelivery";
                    break;
            }

            return View(actionName, model);
        }

        [HttpPost, ActionName("PurchaseOrder")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemOrder(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("PurchaseInvoice")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemInvoice(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("PurchaseDelivery")]
        [FormValueRequiredAttribute("AddLineItem")]
        public ActionResult AddLineItemDelivery(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            AddLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("PurchaseOrder")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemOrder(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("PurchaseInvoice")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemInvoice(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        [HttpPost, ActionName("PurchaseDelivery")]
        [FormValueRequiredAttribute("DeleteLineItem")]
        public ActionResult DeleteLineItemDelivery(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            DeleteLineItem(model);
            return ReturnView(model);
        }

        [NonAction]
        protected void DeleteLineItem(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            var request = HttpContext.Request;
            var deletedItem = request.Form["DeletedLineItem"];
            if (!string.IsNullOrEmpty(deletedItem))
            {
                model.PurchaseLine.PurchaseLineItems.RemoveAt(int.Parse(deletedItem));
            }
        }

        [HttpPost, ActionName("PurchaseOrder")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveOrder(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            PurchaseOrderHeader order = null;
            if (model.Id.HasValue == false || model.Id == 0)
            {
                order = new PurchaseOrderHeader();
            }
            else
            {
                order = _purchasingService.GetPurchaseOrderById(model.Id.Value);
            }
            order.VendorId = model.VendorId.Value;
            order.Date = model.Date;
            order.Description = string.Empty;

            foreach (var line in model.PurchaseLine.PurchaseLineItems)
            {
                var item = _inventoryService.GetItemById(line.ItemId);
                order.PurchaseOrderLines.Add(new PurchaseOrderLine()
                {
                    Amount = line.Price,
                    ItemId = item.Id,
                    Quantity = line.Quantity,
                    MeasurementId = item.PurchaseMeasurementId.Value,
                    Cost = item.Cost.Value,
                });
            }

            _purchasingService.AddPurchaseOrder(order, true);
            return RedirectToAction("PurchaseOrders");
        }

        [HttpPost, ActionName("PurchaseDelivery")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveDelivery(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            if (model.Id == 0)
            {
            }
            else
            {
            }
            return RedirectToAction("PurchaseDeliveries");
        }

        [HttpPost, ActionName("PurchaseInvoice")]
        [FormValueRequiredAttribute("Save")]
        public ActionResult SaveInvoice(Models.ViewModels.Purchases.PurchaseHeaderViewModel model)
        {
            if (model.Id == 0)
            {
                var invoice = new PurchaseInvoiceHeader();
                invoice.Date = model.Date;
                invoice.Description = string.Empty;
                invoice.VendorId = model.VendorId;
                invoice.VendorInvoiceNo = model.ReferenceNo;
            }
            else
            {

            }
            return RedirectToAction("PurchaseInvoices");
        }
    }
}

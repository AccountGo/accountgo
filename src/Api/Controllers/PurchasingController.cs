using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Purchasing;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class PurchasingController : Controller
    {
        private readonly IAdministrationService _adminService;
        private readonly IPurchasingService _purchasingService;
        public PurchasingController(IAdministrationService adminService,
            IPurchasingService purchasingService)
        {
            _adminService = adminService;
            _purchasingService = purchasingService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PurchaseOrders()
        {
            var purchaseOrders = _purchasingService.GetPurchaseOrders();
            IList<Dto.Purchasing.PurchaseOrder> purchaseOrdersDto = new List<Dto.Purchasing.PurchaseOrder>();

            foreach (var purchaseOrder in purchaseOrders)
            {
                var purchaseOrderDto = new Dto.Purchasing.PurchaseOrder()
                {
                    Id = purchaseOrder.Id,
                    PurchaseInvoiceHeaderId = purchaseOrder.PurchaseInvoiceHeaderId,
                    VendorId = purchaseOrder.VendorId.Value,
                    VendorName = purchaseOrder.Vendor.Party.Name,
                    OrderDate = purchaseOrder.Date,
                    Amount = purchaseOrder.PurchaseOrderLines.Sum(l => l.Amount),
                    Completed = purchaseOrder.IsCompleted()
                };

                purchaseOrdersDto.Add(purchaseOrderDto);
            }

            return new ObjectResult(purchaseOrdersDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PurchaseOrder(int id)
        {
            var purchaseOrder = _purchasingService.GetPurchaseOrderById(id);
            Dto.Purchasing.PurchaseOrder purchaseOrderDto = new Dto.Purchasing.PurchaseOrder();

            purchaseOrderDto = new Dto.Purchasing.PurchaseOrder()
            {
                Id = purchaseOrder.Id,
                PurchaseInvoiceHeaderId = purchaseOrder.PurchaseInvoiceHeaderId,                
                VendorId = purchaseOrder.VendorId.Value,
                VendorName = purchaseOrder.Vendor.Party.Name,
                OrderDate = purchaseOrder.Date,
                Amount = purchaseOrder.PurchaseOrderLines.Sum(l => l.Amount),
                Completed = purchaseOrder.IsCompleted()
            };

            foreach(var item in purchaseOrder.PurchaseOrderLines)
            {
                var line = new Dto.Purchasing.PurchaseOrderLine()
                {
                    Id = item.Id,
                    ItemId = item.ItemId,
                    MeasurementId = item.MeasurementId,
                    Quantity = item.Quantity,
                    Amount = item.Amount,
                    Discount = item.Discount
                };

                purchaseOrderDto.PurchaseOrderLines.Add(line);
            }

            return new ObjectResult(purchaseOrderDto);
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SavePurchaseOrder([FromBody]Dto.Purchasing.PurchaseOrder purchaseOrderDto)
        {
            string[] errors = null;

            if (!ModelState.IsValid)
            {
                errors = new string[ModelState.ErrorCount];
                foreach (var val in ModelState.Values)
                    for (int i = 0; i < ModelState.ErrorCount; i++)
                        errors[i] = val.Errors[i].ErrorMessage;

                return new BadRequestObjectResult(errors);
            }

            try
            {
                bool isNew = purchaseOrderDto.Id == 0;
                Core.Domain.Purchases.PurchaseOrderHeader purchaseOrder = null;

                if (isNew)
                {
                    purchaseOrder = new Core.Domain.Purchases.PurchaseOrderHeader();
                }
                else
                {
                    purchaseOrder = _purchasingService.GetPurchaseOrderById(purchaseOrderDto.Id);
                }

                purchaseOrder.VendorId = purchaseOrderDto.VendorId;
                purchaseOrder.Date = purchaseOrderDto.OrderDate;

                foreach (var line in purchaseOrderDto.PurchaseOrderLines)
                {
                    if (!isNew)
                    {
                        var existingLine = purchaseOrder.PurchaseOrderLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (purchaseOrder.PurchaseOrderLines.Where(id => id.Id == line.Id).FirstOrDefault() != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault();
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        }
                        else
                        {
                            var purchaseOrderLine = new Core.Domain.Purchases.PurchaseOrderLine();
                            purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                            purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                            purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            purchaseOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                            purchaseOrder.PurchaseOrderLines.Add(purchaseOrderLine);
                        }
                    }
                    else
                    {
                        var purchaseOrderLine = new Core.Domain.Purchases.PurchaseOrderLine();
                        purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                        purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                        purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                        purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                        purchaseOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                        purchaseOrder.PurchaseOrderLines.Add(purchaseOrderLine);
                    }
                }

                if (isNew)
                {
                    _purchasingService.AddPurchaseOrder(purchaseOrder, true);
                }
                else
                {
                    _purchasingService.UpdatePurchaseOrder(purchaseOrder);
                }

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PurchaseInvoices()
        {
            var purchaseInvoices = _purchasingService.GetPurchaseInvoices();
            IList<Dto.Purchasing.PurchaseInvoice> purchaseInvoicesDto = new List<Dto.Purchasing.PurchaseInvoice>();

            foreach (var purchaseInvoice in purchaseInvoices)
            {
                var purchaseInvoiceDto = new Dto.Purchasing.PurchaseInvoice()
                {
                    Id = purchaseInvoice.Id,
                    VendorId = purchaseInvoice.VendorId.Value,
                    VendorName = purchaseInvoice.Vendor.Party.Name,
                    InvoiceDate = purchaseInvoice.Date,
                    Amount = purchaseInvoice.PurchaseInvoiceLines.Sum(l => l.Amount),
                    IsPaid = purchaseInvoice.IsPaid()
                };

                purchaseInvoicesDto.Add(purchaseInvoiceDto);
            }

            return new ObjectResult(purchaseInvoicesDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PurchaseInvoice(int id)
        {
            var invoice = _purchasingService.GetPurchaseInvoiceById(id);
            var purchaseInvoiceDto = new Dto.Purchasing.PurchaseInvoice()
            {
                Id = invoice.Id,
                InvoiceNo = invoice.No,
                VendorId = invoice.VendorId.GetValueOrDefault(),
                VendorName = invoice.Vendor.Party.Name,
                InvoiceDate = invoice.Date,
                Amount = invoice.PurchaseInvoiceLines.Sum(l => l.Amount)
            };

            foreach (var item in invoice.PurchaseInvoiceLines)
            {
                var line = new Dto.Purchasing.PurchaseInvoiceLine()
                {
                    Id = item.Id,
                    ItemId = item.ItemId,
                    MeasurementId = item.MeasurementId,
                    Quantity = item.Quantity,
                    Amount = item.Amount,
                    Discount = item.Discount
                };

                purchaseInvoiceDto.PurchaseInvoiceLines.Add(line);
            }

            return new ObjectResult(purchaseInvoiceDto);
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SavePurchaseInvoice([FromBody]Dto.Purchasing.PurchaseInvoice purchaseInvoiceDto)
        {
            string[] errors = null;

            if (!ModelState.IsValid)
            {
                errors = new string[ModelState.ErrorCount];
                foreach (var val in ModelState.Values)
                    for (int i = 0; i < ModelState.ErrorCount; i++)
                        errors[i] = val.Errors[i].ErrorMessage;

                return new BadRequestObjectResult(errors);
            }

            try
            {
                bool isNew = purchaseInvoiceDto.Id == 0;
                Core.Domain.Purchases.PurchaseInvoiceHeader purchaseInvoice = null;

                if (isNew)
                {
                    purchaseInvoice = new Core.Domain.Purchases.PurchaseInvoiceHeader();
                }
                else
                {
                    purchaseInvoice = _purchasingService.GetPurchaseInvoiceById(purchaseInvoiceDto.Id);
                }

                purchaseInvoice.VendorId = purchaseInvoiceDto.VendorId;
                purchaseInvoice.Date = purchaseInvoiceDto.InvoiceDate;

                foreach (var line in purchaseInvoiceDto.PurchaseInvoiceLines)
                {
                    if (!isNew)
                    {
                        var existingLine = purchaseInvoice.PurchaseInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault();
                        if (purchaseInvoice.PurchaseInvoiceLines.Where(id => id.Id == line.Id).FirstOrDefault() != null)
                        {
                            existingLine.Amount = line.Amount.GetValueOrDefault();
                            existingLine.Discount = line.Discount.GetValueOrDefault();
                            existingLine.Quantity = line.Quantity.GetValueOrDefault();
                            existingLine.ItemId = line.ItemId.GetValueOrDefault();
                            existingLine.MeasurementId = line.MeasurementId.GetValueOrDefault();
                        }
                        else
                        {
                            var purchaseOrderLine = new Core.Domain.Purchases.PurchaseInvoiceLine();
                            purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                            purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                            purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                            purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                            purchaseOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                            purchaseInvoice.PurchaseInvoiceLines.Add(purchaseOrderLine);
                        }
                    }
                    else
                    {
                        var purchaseOrderLine = new Core.Domain.Purchases.PurchaseInvoiceLine();
                        purchaseOrderLine.Amount = line.Amount.GetValueOrDefault();
                        purchaseOrderLine.Discount = line.Discount.GetValueOrDefault();
                        purchaseOrderLine.Quantity = line.Quantity.GetValueOrDefault();
                        purchaseOrderLine.ItemId = line.ItemId.GetValueOrDefault();
                        purchaseOrderLine.MeasurementId = line.MeasurementId.GetValueOrDefault();

                        purchaseInvoice.PurchaseInvoiceLines.Add(purchaseOrderLine);
                    }
                }

                if (isNew)
                {
                    _purchasingService.AddPurchaseInvoice(purchaseInvoice, 0);
                }
                else
                {
                    //_purchasingService.UpdatePurchaseOrder(purchaseOrder);
                }

                return new OkObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveVendor([FromBody]Dto.Purchasing.Vendor vendorDto)
        {
            bool isNew = vendorDto.Id == 0;
            Core.Domain.Purchases.Vendor vendor = null;

            if (isNew)
            {
                vendor = new Core.Domain.Purchases.Vendor();
                vendor.Party = new Core.Domain.Party();
                vendor.PrimaryContact = new Core.Domain.Contact();
                vendor.PrimaryContact.Party = new Core.Domain.Party();
            }
            else
            {
                vendor = _purchasingService.GetVendorById(vendorDto.Id);
            }

            vendor.No = vendorDto.No;
            vendor.Party.PartyType = Core.Domain.PartyTypes.Vendor;
            vendor.Party.Name = vendorDto.Name;
            vendor.Party.Phone = vendorDto.Phone;
            vendor.Party.Fax = vendorDto.Fax;
            vendor.Party.Email = vendorDto.Email;
            vendor.Party.Website = vendorDto.Website;
            vendor.AccountsPayableAccountId = vendorDto.AccountsPayableAccountId;
            vendor.PurchaseAccountId = vendorDto.PurchaseAccountId;
            vendor.PurchaseDiscountAccountId = vendorDto.PurchaseDiscountAccountId;
            vendor.TaxGroupId = vendorDto.TaxGroupId;
            vendor.PaymentTermId = vendorDto.PaymentTermId;

            if (isNew)
            {
                _purchasingService.AddVendor(vendor);
            }
            else
            {
                _purchasingService.UpdateVendor(vendor);
            }

            return Ok();
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Vendors()
        {
            IList<Dto.Purchasing.Vendor> vendorsDto = new List<Dto.Purchasing.Vendor>();
            try
            {
                var vendors = _purchasingService.GetVendors();
                foreach (var vendor in vendors)
                {
                    var vendorDto = new Dto.Purchasing.Vendor()
                    {
                        Id = vendor.Id,
                        No = vendor.No,
                        Name = vendor.Party.Name,
                        Email = vendor.Party.Email,
                        Phone = vendor.Party.Phone,
                        Fax = vendor.Party.Fax,
                        Website = vendor.Party.Website
                    };

                    vendorsDto.Add(vendorDto);
                }

                return new ObjectResult(vendorsDto);
            }
            catch (Exception ex)
            {
                return new ObjectResult(ex);
            }
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Vendor(int id)
        {
            try
            {
                var vendor = _purchasingService.GetVendorById(id);

                var vendorDto = new Dto.Purchasing.Vendor()
                {
                    Id = vendor.Id,
                    No = vendor.No,
                    Name = vendor.Party.Name,
                    Email = vendor.Party.Email,
                    Phone = vendor.Party.Phone,
                    Fax = vendor.Party.Fax,
                    Website = vendor.Party.Website,
                    AccountsPayableAccountId = vendor.AccountsPayableAccountId.GetValueOrDefault(),
                    PurchaseAccountId = vendor.PurchaseAccountId.GetValueOrDefault(),
                    PurchaseDiscountAccountId = vendor.PurchaseDiscountAccountId.GetValueOrDefault(),
                    TaxGroupId = vendor.TaxGroupId.GetValueOrDefault(),
                    PaymentTermId = vendor.PaymentTermId.GetValueOrDefault(),
                };

                if (vendor.PrimaryContact != null)
                {
                    vendorDto.PrimaryContact.FirstName = vendor.PrimaryContact.FirstName;
                    vendorDto.PrimaryContact.LastName = vendor.PrimaryContact.LastName;
                    vendorDto.PrimaryContact.Party.Email = vendor.PrimaryContact.Party.Email;
                    vendorDto.PrimaryContact.Party.Phone = vendor.PrimaryContact.Party.Phone;
                    vendorDto.PrimaryContact.Party.Fax = vendor.PrimaryContact.Party.Fax;
                    vendorDto.PrimaryContact.Party.Website = vendor.PrimaryContact.Party.Website;
                    vendorDto.PrimaryContact.Party.Name = vendor.PrimaryContact.Party.Name;
                }
                
                return new ObjectResult(vendorDto);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }
    }
}

using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Purchasing;
using System;
using System.Linq;
using System.Collections.Generic;

// For more information on enabling Web API for empty projects, visit http://go.microsoft.com/fwlink/?LinkID=397860

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
                    VendorId = purchaseOrder.VendorId.Value,
                    VendorName = purchaseOrder.Vendor.Party.Name,
                    OrderDate = purchaseOrder.Date,
                    Amount = purchaseOrder.PurchaseOrderLines.Sum(l => l.Amount)
                };

                purchaseOrdersDto.Add(purchaseOrderDto);
            }

            return new ObjectResult(purchaseOrdersDto);
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
        public IActionResult Invoice(int id)
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

            return new ObjectResult(purchaseInvoiceDto);
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

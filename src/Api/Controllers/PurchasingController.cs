using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Purchasing;
using System;
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
                    //Amount = purchaseOrder.SalesOrderLines.Sum(l => l.Amount)
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
                    //Amount = purchaseOrder.SalesOrderLines.Sum(l => l.Amount)
                };

                purchaseInvoicesDto.Add(purchaseInvoiceDto);
            }

            return new ObjectResult(purchaseInvoicesDto);
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
                        //No = vendor.No,
                        Name = vendor.Party.Name,
                        //Email = vendor.Party.Email,
                        //Phone = vendor.Party.Phone,
                        //Fax = vendor.Party.Fax,
                        //Balance = vendor.Balance
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
        [Route("[action]/{id:int}")]
        public IActionResult GetVendorById(int id)
        {
            try
            {
                var vendor = _purchasingService.GetVendorById(id);

                var vendorDto = new Dto.Purchasing.Vendor()
                {
                    Id = vendor.Id,
                    //No = vendor.No,
                    Name = vendor.Party.Name,
                    //Email = vendor.Party.Email,
                    //Phone = vendor.Party.Phone,
                    //Fax = vendor.Party.Fax,
                    //Balance = vendor.Balance
                };

                return new ObjectResult(vendorDto);
            }
            catch (Exception ex)
            {
                return new BadRequestObjectResult(ex);
            }
        }
    }
}

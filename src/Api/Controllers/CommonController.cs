using Microsoft.AspNetCore.Mvc;
using Model.Inventory;
using Model.Purchasing;
using Model.Sales;
using Services.Administration;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using System.Collections.Generic;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class CommonController : Controller
    {
        private readonly ISalesService _salesService;
        private readonly IAdministrationService _administrationService;
        private readonly IInventoryService _inventoryService;
        private readonly IPurchasingService _purchasingService;

        public CommonController(ISalesService salesService,
            IAdministrationService administrationService,
            IInventoryService inventoryService,
            IPurchasingService purchasingService)
        {
            _salesService = salesService;
            _administrationService = administrationService;
            _inventoryService = inventoryService;
            _purchasingService = purchasingService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Customers()
        {
            var customers = _salesService.GetCustomers();
            ICollection<Customer> customersDto = new HashSet<Customer>();

            foreach (var customer in customers)
            {
                if(customer.Party != null)
                    customersDto.Add(new Customer() { Id = customer.Id, Name = customer.Party.Name });
            }

            return new ObjectResult(customersDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PaymentTerms()
        {
            var paymentterms = _administrationService.GetPaymentTerms();
            return new ObjectResult(paymentterms);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Items()
        {
            var items = _inventoryService.GetAllItems();
            ICollection<Item> itemsDto = new HashSet<Item>();

            foreach (var item in items)
                itemsDto.Add(new Item() { Id = item.Id, Description = item.Description });

            return new ObjectResult(itemsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Measurements()
        {
            return new ObjectResult(_inventoryService.GetMeasurements());
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Vendors()
        {
            var vendors = _purchasingService.GetVendors();
            ICollection<Vendor> vendorsDto = new HashSet<Vendor>();

            foreach (var vendor in vendors)
            {
                if(vendor.Party != null)
                    vendorsDto.Add(new Vendor() { Id = vendor.Id, Name = vendor.Party.Name });
            }

            return new ObjectResult(vendorsDto);
        }
    }
}

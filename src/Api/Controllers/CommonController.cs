using Microsoft.AspNetCore.Mvc;
using Dto.Inventory;
using Dto.Purchasing;
using Dto.Sales;
using Services.Administration;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using System.Collections.Generic;
using Services.Financial;
using Dto.Financial;
using System.Linq;
using System;
using Core.Domain;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class CommonController : BaseController
    {
        private readonly ISalesService _salesService;
        private readonly IAdministrationService _administrationService;
        private readonly IInventoryService _inventoryService;
        private readonly IPurchasingService _purchasingService;
        private readonly IFinancialService _financialService;

        public CommonController(ISalesService salesService,
            IAdministrationService administrationService,
            IInventoryService inventoryService,
            IPurchasingService purchasingService,
            IFinancialService financialService)
        {
            _salesService = salesService;
            _administrationService = administrationService;
            _inventoryService = inventoryService;
            _purchasingService = purchasingService;
            _financialService = financialService;
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
                    customersDto.Add(new Customer() { Id = customer.Id, Name = customer.Party.Name, PaymentTermId = customer.PaymentTermId });
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
                itemsDto.Add(new Item() { Id = item.Id, Description = item.Description , Code = item.Code, Price = item.Price, SellMeasurementId = item.SellMeasurementId});

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
                    vendorsDto.Add(new Vendor() { Id = vendor.Id, Name = vendor.Party.Name, PaymentTermId = vendor.PaymentTermId });
            }

            return new ObjectResult(vendorsDto);
        }
        
        [HttpGet]
        [Route("[action]")]
        public IActionResult ItemCategories()
        {
            var itemcategories = _inventoryService.GetItemCategories();
            return Ok(itemcategories.AsEnumerable());
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult CashBanks()
        {
            var banks = _financialService.GetCashAndBanks();
            ICollection<Bank> cashbanksDto = new HashSet<Bank>();

            foreach (var bank in banks)
                cashbanksDto.Add(new Bank() { Id = bank.Id, Name = bank.Name });

            return Ok(cashbanksDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult PostingAccounts()
        {
            var accounts = _financialService.GetAccounts()
                .Where(a => a.ChildAccounts.Count == 0);

            ICollection<Account> accountsDto = new HashSet<Account>();

            foreach (var account in accounts)
                accountsDto.Add(new Account() { Id = account.Id, AccountName = account.AccountName });

            return Ok(accountsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult SalesQuotationStatus()
        {

            List<int> quoteStatuses = new List<int>(new int[] { 0, 1, 3 });

            var salesQuotationsDto = new List<Dto.Common.Status>();
            foreach (var item in Enum.GetValues(typeof(SalesQuoteStatus)))
            {
                if (quoteStatuses.Contains((int)item) )
                {
                    salesQuotationsDto.Add(new Dto.Common.Status { Id = (int)item, Description = Enum.GetName(typeof(SalesQuoteStatus), item) });
                }

            }
            return Json(salesQuotationsDto);
 
        }


    }
}

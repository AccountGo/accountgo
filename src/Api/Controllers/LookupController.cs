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
using Dto.Purchasing.Response;
using Dto.Common.Response;
using Dto.Inventory.Response;
using Microsoft.EntityFrameworkCore;
using Dto.TaxSystem;
using Services.TaxSystem;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class LookupController : BaseController
    {
        private readonly ISalesService _salesService;
        private readonly IAdministrationService _administrationService;
        private readonly IInventoryService _inventoryService;
        private readonly IPurchasingService _purchasingService;
        private readonly IFinancialService _financialService;
        private readonly ITaxService _taxService;
        public LookupController(ISalesService salesService,
            IAdministrationService administrationService,
            IInventoryService inventoryService,
            IPurchasingService purchasingService,
            IFinancialService financialService,
            ITaxService taxService)
        {
            _salesService = salesService;
            _administrationService = administrationService;
            _inventoryService = inventoryService;
            _purchasingService = purchasingService;
            _financialService = financialService;
            _taxService = taxService;
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
            return new ObjectResult(paymentterms.Select(x=> new GetPaymentTerm
            {
                Description = x.Description,
                DueAfterDays = x.DueAfterDays,
                IsActive = x.IsActive,
                PaymentType = (int)x.PaymentType
            }));
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Items()
        {
            var items = _inventoryService.GetItemsLookup()
                .AsNoTracking();

            ICollection<GetItem> itemsDto = new HashSet<GetItem>();

            foreach (var item in items)
            {
                itemsDto.Add(new GetItem()
                {

                    Id = item.Id,
                    Name = item.Description,
                    ProductNo = item.No,
                    SKU = item.Code,
                    ItemTaxGroup = item.ItemTaxGroup == null ? "" : item.ItemTaxGroup.Name,
                    Measurement = new GetMeasurement
                    {
                        Code = item.SellMeasurement.Code,
                        Description = item.SellMeasurement?.Description,
                        Id = item.SellMeasurement.Id
                    },
                    Cost = item.Cost ?? 0,
                    Price = item.Price ?? 0,
                });
            }

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
        public IActionResult Accounts()
        {
            var accounts = _financialService.GetAccountsLookup()
                .Where(x=> x.ParentAccountId != null);

            ICollection<BaseAccount> accountsDto = new HashSet<BaseAccount>();

            foreach (var account in accounts)
            {
                accountsDto.Add(new BaseAccount
                {
                    Id = account.Id,
                    AccountCode = account.AccountCode,
                    AccountName = account.AccountName,
                    AccountClassId = account.AccountClassId,
                    AccountClassName = account.AccountClass.Name
                });
            }

            return Ok(accountsDto);
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Vendors()
        {
            var vendorsDto = new List<GetVendor>();
            try
            {
                var vendors = _purchasingService.GetVendorLookup();
                foreach (var vendor in vendors)
                {
                    var vendorDto = new GetVendor()
                    {
                        Id = vendor.Id,
                        No = vendor.No,
                        Name = vendor.Party.Name,
                        Email = vendor.Party.Email,
                        Phone = vendor.Party.Phone,
                        Fax = vendor.Party.Fax,
                        Website = vendor.Party.Website,
                        Contact = vendor.PrimaryContact.FirstName + " " + vendor.PrimaryContact.LastName,
                        TaxGroup = vendor.TaxGroup == null ? string.Empty : vendor.TaxGroup.Description,
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

        [HttpGet]
        [Route("[action]")]
        public IActionResult ItemTaxGroups()
        {
            var itemTaxGroupsDto = new List<ItemTaxGroup>();
            var itemTaxGroups = _taxService.GetItemTaxGroups();

            foreach (var group in itemTaxGroups)
            {
                var groupDto = new ItemTaxGroup()
                {
                    Id = group.Id,
                    Name = group.Name,
                    IsFullyExempt = group.IsFullyExempt
                };

                itemTaxGroupsDto.Add(groupDto);
            }

            return new ObjectResult(itemTaxGroupsDto);
        }

    }
}

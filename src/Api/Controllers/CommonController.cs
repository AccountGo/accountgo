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
using Services.Banking;
using Dto.Financial;
using System.Linq;
using System;
using Core.Domain;

namespace Api.Controllers
{
    [Core.Tenancy.TenantScoped]
    [Route("api/[controller]")]
    public class CommonController : BaseController
    {
        private readonly ISalesService _salesService;
        private readonly IAdministrationService _administrationService;
        private readonly IInventoryService _inventoryService;
        private readonly IPurchasingService _purchasingService;
        private readonly IFinancialService _financialService;
        private readonly IBankAccountService _bankAccountService;

        public CommonController(ISalesService salesService,
            IAdministrationService administrationService,
            IInventoryService inventoryService,
            IPurchasingService purchasingService,
            IFinancialService financialService,
            IBankAccountService bankAccountService)
        {
            _salesService = salesService;
            _administrationService = administrationService;
            _inventoryService = inventoryService;
            _purchasingService = purchasingService;
            _financialService = financialService;
            _bankAccountService = bankAccountService;
        }

        [HttpGet]
        [Route("Customers")] // api/Common/Customers
        public IActionResult Customers()
        {
            var customers = _salesService.GetCustomers();

            ICollection<Customer> customersDto = new HashSet<Customer>();

            foreach (var customer in customers)
            {
                if (customer.Party != null)
                    customersDto.Add(new Customer() { Id = customer.Id, Name = customer.Party.Name, PaymentTermId = customer.PaymentTermId });
            }

            return new ObjectResult(customersDto);
        }

        [HttpGet]
        [Route("PaymentTerms")] // api/Common/PaymentTerms
        public IActionResult PaymentTerms()
        {
            var paymentterms = _administrationService.GetPaymentTerms();
            return new ObjectResult(paymentterms);
        }

        [HttpGet]
        [Route("Items")] // api/Common/Items
        public IActionResult Items()
        {
            var items = _inventoryService.GetAllItems().OrderBy(i => i.Description);
            return new ObjectResult(items.Select(item => new
            {
                item.Id,
                item.Description,
                item.Code,
                item.Price,
                item.SellMeasurementId,
                Archived = item.ArchivedAt.HasValue,
                TrackStock = item.ItemCategory?.ItemType == Core.Domain.ItemTypes.Purchased,
                item.QuantityOnHand,
                item.AverageCost,
                item.SalesAccountId,
                SalesAccountCode = item.SalesAccount?.AccountCode,
                SalesAccountName = item.SalesAccount?.AccountName
            }).ToList());
        }

        [HttpGet]
        [Route("Measurements")] // api/Common/Measurements
        public IActionResult Measurements()
        {
            return new ObjectResult(_inventoryService.GetMeasurements().OrderBy(m => m.Description));
        }

        [HttpGet]
        [Route("Vendors")] // api/Common/Vendors
        public IActionResult Vendors()
        {
            var vendors = _purchasingService.GetVendors();
            ICollection<Vendor> vendorsDto = new HashSet<Vendor>();

            foreach (var vendor in vendors)
            {
                if (vendor.Party != null)
                    vendorsDto.Add(new Vendor() { Id = vendor.Id, Name = vendor.Party.Name, PaymentTermId = vendor.PaymentTermId });
            }

            return new ObjectResult(vendorsDto);
        }

        [HttpGet]
        [Route("ItemCategories")] // api/Common/ItemCategories
        public IActionResult ItemCategories()
        {
            var itemcategories = _inventoryService.GetItemCategories();
            return Ok(itemcategories.AsEnumerable());
        }

        [HttpGet]
        [Route("CashBanks")] // api/Common/CashBanks
        public IActionResult CashBanks()
        {
            // #413 1B: active banks only via BankAccountService (archived excluded).
            var banks = _bankAccountService.List(includeArchived: false, includeFullNumbers: false).Accounts;
            ICollection<Bank> cashbanksDto = new HashSet<Bank>();

            foreach (var bank in banks)
                cashbanksDto.Add(new Bank() { Id = bank.Id, Name = bank.Name });

            return Ok(cashbanksDto);
        }

        [HttpGet]
        [Route("PostingAccounts")] // api/Common/PostingAccounts
        public IActionResult PostingAccounts()
        {
            var accounts = _financialService.GetAccounts()
                .Where(a => a.ChildAccounts.Count == 0)
                .OrderBy(a => a.AccountName);

            ICollection<Account> accountsDto = new HashSet<Account>();

            foreach (var account in accounts)
                accountsDto.Add(new Account() { Id = account.Id, AccountCode = account.AccountCode, AccountName = account.AccountName });

            return Ok(accountsDto);
        }

        [HttpGet]
        [Route("PaymentMethods")] // api/Common/PaymentMethods
        public IActionResult PaymentMethods()
        {
            var paymentMethods = new List<Dto.Common.Status>
            {
                new Dto.Common.Status { Id = 1, Description = "Check" },
                new Dto.Common.Status { Id = 2, Description = "E-Transfer" },
                new Dto.Common.Status { Id = 3, Description = "Cash" },
                new Dto.Common.Status { Id = 4, Description = "Credit Card" },
                new Dto.Common.Status { Id = 5, Description = "Other" }
            };

            return Json(paymentMethods);
        }

    }
}

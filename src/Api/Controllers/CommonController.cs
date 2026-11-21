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
using Core.Domain.Security;
using Core.Authorization;
using Api.Filters;

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
        [RequireModule(SystemModules.Sales)]
        [RequirePermission(SystemPermissions.SalesCustomersView)]
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
        [RequireOperation(AccessOperationCatalog.WorkspaceCurrentView)]
        public IActionResult PaymentTerms()
        {
            var paymentterms = _administrationService.GetPaymentTerms()
                .Select(pt => new CommonPaymentTermDto(pt.Id, pt.Description, pt.PaymentType, pt.DueAfterDays, pt.IsActive))
                .ToList();
            return new ObjectResult(paymentterms);
        }

        [HttpGet]
        [Route("Items")] // api/Common/Items
        [RequireModule(SystemModules.Inventory)]
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Items()
        {
            var items = _inventoryService.GetAllItems().OrderBy(i => i.Description);
            return new ObjectResult(items.Select(item => new CommonItemDto(
                item.Id,
                item.Description,
                item.Code,
                item.Price,
                item.SellMeasurementId,
                item.ArchivedAt.HasValue,
                item.ItemCategory?.ItemType == Core.Domain.ItemTypes.Purchased,
                item.QuantityOnHand,
                item.AverageCost,
                item.SalesAccountId,
                item.SalesAccount?.AccountCode,
                item.SalesAccount?.AccountName
            )).ToList());
        }

        [HttpGet]
        [Route("Measurements")] // api/Common/Measurements
        [RequireModule(SystemModules.Inventory)]
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult Measurements()
        {
            return new ObjectResult(_inventoryService.GetMeasurements()
                .OrderBy(m => m.Description)
                .Select(m => new CommonMeasurementDto(m.Id, m.Code, m.Description))
                .ToList());
        }

        [HttpGet]
        [Route("Vendors")] // api/Common/Vendors
        [RequireModule(SystemModules.Purchases)]
        [RequirePermission(SystemPermissions.PurchasesVendorsView)]
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
        [RequireModule(SystemModules.Inventory)]
        [RequirePermission(SystemPermissions.InventoryItemsView)]
        public IActionResult ItemCategories()
        {
            var itemcategories = _inventoryService.GetItemCategories()
                .Select(c => new CommonItemCategoryDto(
                    c.Id,
                    c.Name,
                    c.ItemType,
                    c.MeasurementId,
                    c.SalesAccountId,
                    c.InventoryAccountId,
                    c.CostOfGoodsSoldAccountId,
                    c.AdjustmentAccountId,
                    c.AssemblyAccountId))
                .ToList();
            return Ok(itemcategories);
        }

        [HttpGet]
        [Route("CashBanks")] // api/Common/CashBanks
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.BankingAccountsView)]
        public IActionResult CashBanks()
        {
            // #413 1B: active banks only via BankAccountService (archived excluded).
            var banks = _bankAccountService.List(
                includeArchived: false,
                includeFullNumbers: false,
                includeCashHistoryCodes: false).Accounts;
            ICollection<Bank> cashbanksDto = new HashSet<Bank>();

            foreach (var bank in banks)
                cashbanksDto.Add(new Bank() { Id = bank.Id, Name = bank.Name });

            return Ok(cashbanksDto);
        }

        [HttpGet]
        [Route("PostingAccounts")] // api/Common/PostingAccounts
        [RequireModule(SystemModules.Financials)]
        [RequirePermission(SystemPermissions.FinancialsAccountsView)]
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
        [RequireOperation(AccessOperationCatalog.WorkspaceCurrentView)]
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

        private sealed record CommonPaymentTermDto(
            [property: Dto.Common.PublicId("term")] int Id,
            string? Description,
            PaymentTypes PaymentType,
            int? DueAfterDays,
            bool IsActive);

        private sealed record CommonItemCategoryDto(
            [property: Dto.Common.PublicId("icat")] int Id,
            string? Name,
            ItemTypes ItemType,
            [property: Dto.Common.PublicId("uom")] int? MeasurementId,
            [property: Dto.Common.PublicId("acct")] int? SalesAccountId,
            [property: Dto.Common.PublicId("acct")] int? InventoryAccountId,
            [property: Dto.Common.PublicId("acct")] int? CostOfGoodsSoldAccountId,
            [property: Dto.Common.PublicId("acct")] int? AdjustmentAccountId,
            [property: Dto.Common.PublicId("acct")] int? AssemblyAccountId);

        private sealed record CommonMeasurementDto(
            [property: Dto.Common.PublicId("uom")] int Id,
            string? Code,
            string? Description);

        private sealed record CommonItemDto(
            [property: Dto.Common.PublicId("item")] int Id,
            string? Description,
            string? Code,
            decimal? Price,
            [property: Dto.Common.PublicId("uom")] int? SellMeasurementId,
            bool Archived,
            bool TrackStock,
            decimal QuantityOnHand,
            decimal AverageCost,
            [property: Dto.Common.PublicId("acct")] int? SalesAccountId,
            string? SalesAccountCode,
            string? SalesAccountName);
    }
}

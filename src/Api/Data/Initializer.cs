using System;
using System.Collections.Generic;
using System.Linq;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using Services.Security;

namespace Api.Data
{
    public class Initializer
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IInventoryService _inventoryService;
        private readonly ISecurityService _securityService;

        public Initializer(IAdministrationService adminService,
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IInventoryService inventoryService,
            ISecurityService securityService)
        {
            _adminService = adminService;
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _inventoryService = inventoryService;
            _securityService = securityService;
        }
        public bool Setup()
        {
            // Clear the database in this order
            // 1.Company
            // 2.Chart of accounts/account classes
            // 3.Financial year
            // 4.Payment terms
            // 5.GL setting
            // 6.Tax
            // 7.Vendor
            // 8.Customer
            // 9.Items
            // 10.Banks
            // 11.Security Roles
            try
            {
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Setup() - An error occured during initialization of database. Clear() method will be called to rollback any changes. Error Message = " + ex.Message);
                Clear();
                return false;
            }
        }
        public bool Clear()
        {
            // Clear the database in this order
            try
            {
                // Security Roles
                // if (_securityService.GetRole("SystemAdministrators").Id > 0)
                //     _securityService.DeleteRole(_securityService.GetRole("SystemAdministrators").Id);
                // if (_securityService.GetRole("GeneralUsers").Id > 0)
                //     _securityService.DeleteRole(_securityService.GetRole("SystemAdministrators").Id);

                // Banks

                // Items

                // Customers

                // Vendor

                // Tax

                // GL setting

                // Payment terms

                // Financial year

                // Chart of accounts / account classes

                // Company
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Clear() - An error occured during clearing database. You may need to re-create the database and run Setup() again. Error Message = " + ex.Message);
                return false;
            }
        }

        private void SetupSecurityRoles()
        {
            var existingSecurityRoles = _securityService.GetAllSecurityRole();
            if (existingSecurityRoles.FirstOrDefault(r => r.Name == "SystemAdministrators") == null)
                _securityService.AddRole("SystemAdministrators");
            if (existingSecurityRoles.FirstOrDefault(r => r.Name == "GeneralUsers") == null)
                _securityService.AddRole("GeneralUsers");
        }

        private void SetupBanks() // Dependency: chart of account
        {
            IList<Core.Domain.Financials.Bank> banks = new List<Core.Domain.Financials.Bank>();

            var bank1 = new Core.Domain.Financials.Bank
            {
                AccountId = _financialService.GetAccountByAccountCode("10111").Id,
                Name = "General Fund",
                Type = Core.Domain.BankTypes.CheckingAccount,
                BankName = "GFB",
                Number = "1234567890",
                Address = "123 Main St.",
                IsDefault = true,
                IsActive = true,
            };
            var bank2 = new Core.Domain.Financials.Bank
            {
                AccountId = _financialService.GetAccountByAccountCode("10113").Id,
                Name = "Petty Cash Account",
                Type = Core.Domain.BankTypes.CashAccount,
                IsDefault = false,
                IsActive = true,
            };

            banks.Add(bank1);
            banks.Add(bank2);

            foreach (var b in banks)
                _financialService.SaveBank(b);
        }
    }
}
﻿using Dto.Administration;
using Microsoft.AspNetCore.Mvc;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using System;
using System.Linq;
using System.Collections.Generic;

namespace Api.Controllers
{
    [Route("api/[controller]")]
    public class AdministrationController : BaseController
    {
        private readonly IAdministrationService _adminService;
        private readonly IFinancialService _financialService;
        private readonly ISalesService _salesService;
        private readonly IPurchasingService _purchasingService;
        private readonly IInventoryService _inventoryService;

        public AdministrationController(IAdministrationService adminService,
            IFinancialService financialService,
            ISalesService salesService,
            IPurchasingService purchasingService,
            IInventoryService inventoryService)
        {
            _adminService = adminService;
            _financialService = financialService;
            _salesService = salesService;
            _purchasingService = purchasingService;
            _inventoryService = inventoryService;
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult InitializedCompany()
        {
            string[] errors = null;
            try
            {
                if (!_adminService.IsSystemInitialized())
                {
                    InitializedData();
                }
                return new ObjectResult(Ok());
            }
            catch (Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }            
        }

        [HttpGet]
        [Route("[action]")]
        public IActionResult Company(string companyCode)
        {
            if (string.IsNullOrEmpty(companyCode))
                return new ObjectResult(_adminService.GetDefaultCompany());
            else
                return new ObjectResult(_adminService.GetDefaultCompany());
        }

        [HttpPost]
        [Route("[action]")]
        public IActionResult SaveCompany([FromBody]Company companyDto)
        {
            string[] errors = null;
            try
            {
                if (!ModelState.IsValid)
                {
                    errors = new string[ModelState.ErrorCount];
                    foreach (var val in ModelState.Values)
                        for (int i = 0; i < ModelState.ErrorCount; i++)
                            errors[i] = val.Errors[i].ErrorMessage;
                    return new BadRequestObjectResult(errors);
                }

                Core.Domain.Company company = null;

                if (companyDto.Id == 0)
                {
                    company = new Core.Domain.Company();
                }
                else
                {
                    company = _adminService.GetDefaultCompany();
                }
                
                company.CompanyCode = companyDto.CompanyCode;
                company.Name = companyDto.Name;
                company.ShortName = companyDto.ShortName;

                _adminService.SaveCompany(company);

                return new ObjectResult(Ok());
            }
            catch(Exception ex)
            {
                errors = new string[1] { ex.InnerException != null ? ex.InnerException.Message : ex.Message };
                return new BadRequestObjectResult(errors);
            }
        }

        private void InitializedData()
        {
            /*
             * 1.Company
             * 2.Chart of accounts/account classes
             * 3.Financial year
             * 4.Payment terms
             * 5.GL setting
             * 6.Tax
             * 7.Vendor
             * 8.Customer
             * 9.Items
             */
            try
            {
                // 1.Company
                Core.Domain.Company company = null;
                if (_adminService.GetDefaultCompany() == null)
                {
                    company = new Core.Domain.Company()
                    {
                        Name = "Financial Solutions Inc.",
                        CompanyCode = "100",
                        ShortName = "FSI",
                    };
                    _adminService.SaveCompany(company);
                }                

                //2.Chart of accounts/account classes
                IList<Core.Domain.Financials.AccountClass> accountClasses = new List<Core.Domain.Financials.AccountClass>();
                if(_financialService.GetAccounts().Count() < 1)
                {
                    string[,] values = LoadCsv(Resource.ResourceManager.GetString("coa").Split(';')[0]);
                    List<Core.Domain.Financials.Account> accounts = new List<Core.Domain.Financials.Account>();

                    for (int i = 1; i < (values.Length / 8); i++)
                    {
                        Core.Domain.Financials.Account account = new Core.Domain.Financials.Account();
                        account.AccountCode = values[i, 0].ToString();
                        account.AccountName = values[i, 1].ToString();
                        account.AccountClassId = int.Parse(values[i, 3].ToString());
                        account.IsCash = bool.Parse(values[i, 5].ToString());
                        account.IsContraAccount = bool.Parse(values[i, 4].ToString());

                        if (values[i, 7].ToString() == "DR")
                            account.DrOrCrSide = Core.Domain.DrOrCrSide.Dr;
                        else if (values[i, 7].ToString() == "CR")
                            account.DrOrCrSide = Core.Domain.DrOrCrSide.Cr;
                        else
                            account.DrOrCrSide = Core.Domain.DrOrCrSide.NA;

                        account.CompanyId = 1;
                        accounts.Add(account);
                    }

                    for (int i = 1; i < (values.Length / 8); i++)
                    {
                        string accountCode = values[i, 0].ToString();
                        string parentAccountCode = values[i, 2].ToString();

                        var account = accounts.Where(a => a.AccountCode == accountCode).FirstOrDefault();
                        var parentAccount = accounts.Where(a => a.AccountCode == parentAccountCode).FirstOrDefault();
                        if (parentAccount != null)
                            account.ParentAccount = parentAccount;
                    }
                    
                    var assetClass = new Core.Domain.Financials.AccountClass() { Name = "Assets", NormalBalance = "Dr" };
                    var liabilitiesClass = new Core.Domain.Financials.AccountClass() { Name = "Liabilities", NormalBalance = "Cr" };
                    var equityClass = new Core.Domain.Financials.AccountClass() { Name = "Equity", NormalBalance = "Cr" };
                    var revenueClass = new Core.Domain.Financials.AccountClass() { Name = "Revenue", NormalBalance = "Cr" };
                    var expenseClass = new Core.Domain.Financials.AccountClass() { Name = "Expense", NormalBalance = "Dr" };
                    var temporaryClass = new Core.Domain.Financials.AccountClass() { Name = "Temporary", NormalBalance = "NA" };

                    accountClasses.Add(assetClass);
                    accountClasses.Add(liabilitiesClass);
                    accountClasses.Add(equityClass);
                    accountClasses.Add(revenueClass);
                    accountClasses.Add(expenseClass);
                    accountClasses.Add(temporaryClass);

                    foreach (var account in accounts)
                    {
                        switch (account.AccountClassId)
                        {
                            case 1:
                                assetClass.Accounts.Add(account);
                                break;
                            case 2:
                                liabilitiesClass.Accounts.Add(account);
                                break;
                            case 3:
                                equityClass.Accounts.Add(account);
                                break;
                            case 4:
                                revenueClass.Accounts.Add(account);
                                break;
                            case 5:
                                expenseClass.Accounts.Add(account);
                                break;
                            case 6:
                                temporaryClass.Accounts.Add(account);
                                break;
                        }
                    }
                    _financialService.SaveAccountClasses(accountClasses);
                }                

                //3.Financial year
                Core.Domain.Financials.FinancialYear financialYear = null;
                if (_adminService.GetFinancialYears().Count < 1)
                {
                    financialYear = new Core.Domain.Financials.FinancialYear()
                    {
                        FiscalYearCode = "FY1516",
                        FiscalYearName = "FY 2016/2017",
                        StartDate = new DateTime(2016, 01, 01),
                        EndDate = new DateTime(2016, 12, 31),
                        IsActive = true
                    };
                    _financialService.SaveFinancialYear(financialYear);
                }                

                //4.Payment terms
                IList<Core.Domain.PaymentTerm> paymentTerms = new List<Core.Domain.PaymentTerm>();
                if (_adminService.GetPaymentTerms().Count < 1)
                {
                    paymentTerms.Add(new Core.Domain.PaymentTerm()
                    {
                        Description = "Payment due within 10 days",
                        PaymentType = Core.Domain.PaymentTypes.AfterNoOfDays,
                        DueAfterDays = 10,
                        IsActive = true,
                    });
                    paymentTerms.Add(new Core.Domain.PaymentTerm()
                    {
                        Description = "Due 15th Of the Following Month 	",
                        PaymentType = Core.Domain.PaymentTypes.DayInTheFollowingMonth,
                        DueAfterDays = 15,
                        IsActive = true,
                    });
                    paymentTerms.Add(new Core.Domain.PaymentTerm()
                    {
                        Description = "Cash Only",
                        PaymentType = Core.Domain.PaymentTypes.Cash,
                        IsActive = true,
                    });

                    foreach(var p in paymentTerms)
                    {
                        _financialService.SavePaymentTerm(p);
                    }
                }
                

                //5.GL setting
                Core.Domain.Financials.GeneralLedgerSetting glSetting = null;
                if (_financialService.GetGeneralLedgerSetting() == null)
                {
                    glSetting = new Core.Domain.Financials.GeneralLedgerSetting()
                    {
                        Company = company,
                        GoodsReceiptNoteClearingAccount = _financialService.GetAccountByAccountCode("10810"),
                        ShippingChargeAccount = _financialService.GetAccountByAccountCode("40500"),
                        SalesDiscountAccount = _financialService.GetAccountByAccountCode("40400"),
                    };
                }
                _financialService.SaveGeneralLedgerSetting(glSetting);

                //6.Tax
                IList<Core.Domain.TaxSystem.Tax> taxes = new List<Core.Domain.TaxSystem.Tax>();
                if (_financialService.GetTaxes().Count() < 1)
                {
                    var salesTaxAccount = _financialService.GetAccountByAccountCode("20300");
                    var purchaseTaxAccount = _financialService.GetAccountByAccountCode("50700");

                    var vat5 = new Core.Domain.TaxSystem.Tax()
                    {
                        TaxCode = "VAT5%",
                        TaxName = "VAT 5%",
                        Rate = 5,
                        IsActive = true,
                        SalesAccountId = salesTaxAccount.Id,
                        PurchasingAccountId = purchaseTaxAccount.Id,
                    };

                    var vat10 = new Core.Domain.TaxSystem.Tax()
                    {
                        TaxCode = "VAT10%",
                        TaxName = "VAT 10%",
                        Rate = 10,
                        IsActive = true,
                        SalesAccountId = salesTaxAccount.Id,
                        PurchasingAccountId = purchaseTaxAccount.Id,
                    };

                    var evat12 = new Core.Domain.TaxSystem.Tax()
                    {
                        TaxCode = "VAT12%",
                        TaxName = "VAT 12%",
                        Rate = 12,
                        IsActive = true,
                        SalesAccountId = salesTaxAccount.Id,
                        PurchasingAccountId = purchaseTaxAccount.Id,
                    };

                    var exportTax1 = new Core.Domain.TaxSystem.Tax()
                    {
                        TaxCode = "exportTax1%",
                        TaxName = "Export Tax 1%",
                        Rate = 1,
                        IsActive = true,
                        SalesAccountId = salesTaxAccount.Id,
                        PurchasingAccountId = purchaseTaxAccount.Id,
                    };

                    var taxGroupVAT = new Core.Domain.TaxSystem.TaxGroup()
                    {
                        Description = "VAT",
                        TaxAppliedToShipping = false,
                        IsActive = true,
                    };

                    var taxGroupExport = new Core.Domain.TaxSystem.TaxGroup()
                    {
                        Description = "Export",
                        TaxAppliedToShipping = false,
                        IsActive = true,
                    };

                    IList<Core.Domain.TaxSystem.TaxGroup> taxGroups = new List<Core.Domain.TaxSystem.TaxGroup>();
                    taxGroups.Add(taxGroupVAT);
                    taxGroups.Add(taxGroupExport);

                    var itemTaxGroupRegular = new Core.Domain.TaxSystem.ItemTaxGroup()
                    {
                        Name = "Regular",
                        IsFullyExempt = false,
                    };

                    var itemTaxGroupRegularPreferenced = new Core.Domain.TaxSystem.ItemTaxGroup()
                    {
                        Name = "Preferenced",
                        IsFullyExempt = false,
                    };

                    IList<Core.Domain.TaxSystem.ItemTaxGroup> itemtaxGroups = new List<Core.Domain.TaxSystem.ItemTaxGroup>();
                    itemtaxGroups.Add(itemTaxGroupRegular);
                    itemtaxGroups.Add(itemTaxGroupRegularPreferenced);

                    vat5.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax()
                    {
                        TaxGroup = taxGroupVAT,
                    });

                    evat12.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax()
                    {
                        TaxGroup = taxGroupVAT,
                    });

                    exportTax1.TaxGroupTaxes.Add(new Core.Domain.TaxSystem.TaxGroupTax()
                    {
                        TaxGroup = taxGroupExport,
                    });

                    vat5.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax()
                    {
                        ItemTaxGroup = itemTaxGroupRegularPreferenced,
                        IsExempt = false,
                    });

                    evat12.ItemTaxGroupTaxes.Add(new Core.Domain.TaxSystem.ItemTaxGroupTax()
                    {
                        ItemTaxGroup = itemTaxGroupRegular,
                        IsExempt = false,
                    });

                    taxes.Add(vat5);
                    taxes.Add(vat10);
                    taxes.Add(evat12);
                    taxes.Add(exportTax1);

                    foreach(var tax in taxes)
                    {
                        _adminService.AddNewTax(tax);
                    }
                }

                Core.Domain.Purchases.Vendor vendor = new Core.Domain.Purchases.Vendor();
                if (_purchasingService.GetVendors().Count() < 1)
                {
                    Core.Domain.Party vendorParty = new Core.Domain.Party();
                    vendorParty.Name = "ABC Sample Supplier";
                    vendorParty.PartyType = Core.Domain.PartyTypes.Vendor;
                    vendorParty.IsActive = true;
                    
                    vendor.AccountsPayableAccountId = _financialService.GetAccountByAccountCode("20110").Id;
                    vendor.PurchaseAccountId = _financialService.GetAccountByAccountCode("50200").Id;
                    vendor.PurchaseDiscountAccountId = _financialService.GetAccountByAccountCode("50400").Id;
                    vendor.Party = vendorParty;

                    Core.Domain.Contact primaryContact = new Core.Domain.Contact();
                    primaryContact.ContactType = Core.Domain.ContactTypes.Vendor;
                    primaryContact.FirstName = "Mary";
                    primaryContact.LastName = "Walter";

                    primaryContact.Party = new Core.Domain.Party();
                    primaryContact.Party.Name = "Mary Walter";
                    primaryContact.Party.PartyType = Core.Domain.PartyTypes.Contact;

                    vendor.PrimaryContact = primaryContact;

                    _purchasingService.AddVendor(vendor);
                }                

                //8.Customer
                Core.Domain.Sales.Customer customer = new Core.Domain.Sales.Customer();
                if (_salesService.GetCustomers().Count() < 1)
                {
                    var accountAR = _financialService.GetAccountByAccountCode("10120");
                    var accountSales = _financialService.GetAccountByAccountCode("40100");
                    var accountAdvances = _financialService.GetAccountByAccountCode("20120");
                    var accountSalesDiscount = _financialService.GetAccountByAccountCode("40400");

                    Core.Domain.Party customerParty = new Core.Domain.Party();
                    customerParty.Name = "ABC Customer";
                    customerParty.PartyType = Core.Domain.PartyTypes.Customer;
                    customerParty.IsActive = true;

                    customer.AccountsReceivableAccountId = accountAR != null ? (int?)accountAR.Id : null;
                    customer.SalesAccountId = accountSales != null ? (int?)accountSales.Id : null;
                    customer.CustomerAdvancesAccountId = accountAdvances != null ? (int?)accountAdvances.Id : null;
                    customer.SalesDiscountAccountId = accountSalesDiscount != null ? (int?)accountSalesDiscount.Id : null;
                    customer.TaxGroupId = _financialService.GetTaxGroups().Where(tg => tg.Description == "VAT").FirstOrDefault().Id;
                    customer.Party = customerParty;

                    Core.Domain.Contact primaryContact = new Core.Domain.Contact();
                    primaryContact.ContactType = Core.Domain.ContactTypes.Customer;
                    primaryContact.FirstName = "John";
                    primaryContact.LastName = "Doe";

                    primaryContact.Party = new Core.Domain.Party();
                    primaryContact.Party.Name = "John Doe";
                    primaryContact.Party.PartyType = Core.Domain.PartyTypes.Contact;

                    customer.PrimaryContact = primaryContact;

                    _salesService.AddCustomer(customer);
                }                

                // 9.Items
                IList<Core.Domain.Items.Measurement> measurements = new List<Core.Domain.Items.Measurement>();
                IList<Core.Domain.Items.ItemCategory> categories = new List<Core.Domain.Items.ItemCategory>();

                if (_inventoryService.GetAllItems().Count() < 1)
                {
                    measurements.Add(new Core.Domain.Items.Measurement() { Code = "EA", Description = "Each" });
                    measurements.Add(new Core.Domain.Items.Measurement() { Code = "PK", Description = "Pack" });
                    measurements.Add(new Core.Domain.Items.Measurement() { Code = "MO", Description = "Monthly" });
                    measurements.Add(new Core.Domain.Items.Measurement() { Code = "HR", Description = "Hour" });

                    foreach(var m in measurements)
                    {
                        _inventoryService.SaveMeasurement(m);
                    }

                    // Accounts = Sales A/C (40100), Inventory (10800), COGS (50300), Inv Adjustment (50500), Item Assm Cost (10900)
                    var sales = _financialService.GetAccountByAccountCode("40100");
                    var inventory = _financialService.GetAccountByAccountCode("10800");
                    var invAdjusment = _financialService.GetAccountByAccountCode("50500");
                    var cogs = _financialService.GetAccountByAccountCode("50300");
                    var assemblyCost = _financialService.GetAccountByAccountCode("10900");

                    var chargesCategory = new Core.Domain.Items.ItemCategory()
                    {
                        Name = "Charges",
                        Measurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        ItemType = Core.Domain.ItemTypes.Charge,
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        AdjustmentAccount = invAdjusment,
                        CostOfGoodsSoldAccount = cogs,
                        AssemblyAccount = assemblyCost,
                    };

                    chargesCategory.Items.Add(new Core.Domain.Items.Item()
                    {
                        Description = "HOA Dues",
                        SellDescription = "HOA Dues",
                        PurchaseDescription = "HOA Dues",
                        Price = 350,
                        SmallestMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "MO").FirstOrDefault(),
                        SellMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "MO").FirstOrDefault(),
                        SalesAccount = _financialService.GetAccountByAccountCode("40200"),
                        No = "0001",
                        Code = "1111"
                    });

                    categories.Add(chargesCategory);

                    var componentCategory = new Core.Domain.Items.ItemCategory()
                    {
                        Name = "Components",
                        Measurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        ItemType = Core.Domain.ItemTypes.Purchased,
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        AdjustmentAccount = invAdjusment,
                        CostOfGoodsSoldAccount = cogs,
                        AssemblyAccount = assemblyCost,
                    };

                    var carStickerItem = new Core.Domain.Items.Item()
                    {
                        Description = "Car Sticker",
                        SellDescription = "Car Sticker",
                        PurchaseDescription = "Car Sticker",
                        Price = 100,
                        Cost = 40,
                        SmallestMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        SellMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        PurchaseMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        CostOfGoodsSoldAccount = cogs,
                        InventoryAdjustmentAccount = invAdjusment,
                        ItemTaxGroup = _financialService.GetItemTaxGroups().Where(m => m.Name == "Regular").FirstOrDefault(),
                        No = "0002",
                        Code = "2222"
                    };

                    var otherItem = new Core.Domain.Items.Item()
                    {
                        Description = "Optical Mouse",
                        SellDescription = "Optical Mouse",
                        PurchaseDescription = "Optical Mouse",
                        Price = 80,
                        Cost = 30,
                        SmallestMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        SellMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        PurchaseMeasurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        CostOfGoodsSoldAccount = cogs,
                        InventoryAdjustmentAccount = invAdjusment,
                        ItemTaxGroup = _financialService.GetItemTaxGroups().Where(m => m.Name == "Regular").FirstOrDefault(),
                        No = "0003",
                        Code = "3333"
                    };

                    componentCategory.Items.Add(carStickerItem);
                    componentCategory.Items.Add(otherItem);

                    categories.Add(componentCategory);

                    categories.Add(new Core.Domain.Items.ItemCategory()
                    {
                        Name = "Services",
                        Measurement = _inventoryService.GetMeasurements().Where(m => m.Code == "HR").FirstOrDefault(),
                        ItemType = Core.Domain.ItemTypes.Service,
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        AdjustmentAccount = invAdjusment,
                        CostOfGoodsSoldAccount = cogs,
                        AssemblyAccount = assemblyCost,
                    });

                    categories.Add(new Core.Domain.Items.ItemCategory()
                    {
                        Name = "Systems",
                        Measurement = _inventoryService.GetMeasurements().Where(m => m.Code == "EA").FirstOrDefault(),
                        ItemType = Core.Domain.ItemTypes.Manufactured,
                        SalesAccount = sales,
                        InventoryAccount = inventory,
                        AdjustmentAccount = invAdjusment,
                        CostOfGoodsSoldAccount = cogs,
                        AssemblyAccount = assemblyCost,
                    });

                    foreach(var c in categories)
                    {
                        _inventoryService.SaveItemCategory(c);
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;               
            }
            System.Diagnostics.Debug.Write("Completed");
        }

        private string[,] LoadCsv(string filename)
        {
            // Get the file's text.
            string whole_file = System.IO.File.ReadAllText(filename);

            // Split into lines.
            whole_file = whole_file.Replace('\n', '\r');
            string[] lines = whole_file.Split(new char[] { '\r' },
                StringSplitOptions.RemoveEmptyEntries);

            // See how many rows and columns there are.
            int num_rows = lines.Length;
            int num_cols = lines[0].Split(',').Length;

            // Allocate the data array.
            string[,] values = new string[num_rows, num_cols];

            // Load the array.
            for (int r = 0; r < num_rows; r++)
            {
                string[] line_r = lines[r].Split(',');
                for (int c = 0; c < num_cols; c++)
                {
                    values[r, c] = line_r[c];
                }
            }

            // Return the values.
            return values;
        }
    }
}

﻿using Core.Domain;
using Core.Domain.Auditing;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using System.Collections.Generic;
using System.Linq;

namespace Api.Data
{
    public static class DbInitializerHelper
    {
        public static ApiDbContext _context;
        public static string _filename;

        static DbInitializerHelper()
        {
        }
        //public static void Initialize()
        //{
        //    try
        //    {
        //        using (_context = new ApiDbContext())
        //        {

        //            if (_context.AuditableEntities.Count() == 0)
        //                InitializeEntityToAudit();

        //            if (string.IsNullOrEmpty(_filename))
        //                _filename = AppDomain.CurrentDomain.BaseDirectory + "/App_Data/coa.csv";

        //            //DbInitializerHelper.InsertAdminUser();

        //            if (_context.Users.Count() == 0)
        //                DbInitializerHelper.InitialUserAndSecurityModel();

        //            Company company = null;
        //            if (_context.Companies.Count() == 0)
        //                company = DbInitializerHelper.CreateDefaultCompany();
        //            else
        //                company = _context.Companies.FirstOrDefault();

        //            FinancialYear fy = null;
        //            if (_context.FiscalYears.Count() == 0)
        //                fy = InitFiscalYear();

        //            List<PaymentTerm> paymentTerms = null;
        //            if (_context.PaymentTerms.Count() == 0)
        //                paymentTerms = InitPaymentTerms();

        //            if (_context.AccountClasses.Count() == 0)
        //                DbInitializerHelper.InitializeAccountClasses();

        //            if (_context.Accounts.Count() == 0)
        //            {
        //                DbInitializerHelper.LoadChartOfAccountsFromFile(_filename, company.Id);
        //                DbInitializerHelper.UpdateAccountsParentCodes(_filename);
        //            }

        //            GeneralLedgerSetting glSetting = null;
        //            if (_context.GeneralLedgerSettings.Count() == 0)
        //                glSetting = InitGeneralLedgerSetting();

        //            if (_context.Taxes.Count() == 0)
        //                DbInitializerHelper.InitTax();

        //            List<Bank> banks = null;
        //            if (_context.Banks.Count() == 0)
        //                banks = InitBanks();

        //            Vendor vendor = null;
        //            if (_context.Vendors.Count() == 0)
        //                vendor = DbInitializerHelper.InitVendor();

        //            Customer customer = null;
        //            if (_context.Customers.Count() == 0)
        //                customer = DbInitializerHelper.InitCustomer();

        //            List<Item> items = null;
        //            if (_context.Items.Count() == 0)
        //                items = InitItems();
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        throw ex;
        //    }
        //    finally
        //    {
        //        _context.Dispose();
        //    }
        //}

        //public static void InsertAdminUser()
        //{
        //    string insertUser = "IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetUsers]) BEGIN INSERT[dbo].[AspNetUsers]([Id], [Email], [EmailConfirmed], [PasswordHash], [SecurityStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEndDateUtc], [LockoutEnabled], [AccessFailedCount], [UserName])";
        //    insertUser += "VALUES(N'c12b7ec4-72ab-4960-918a-1a397b15dfd7', N'admin@email.com', 0, N'AMAeZqmeyoa5vcvxJ3f3B6YEgLImAbzrlleAXKX2zer/C0TVx0J6gjzyo47yU5YwGw==', N'8b035264-31e2-4b8f-8ea1-2c608debcd14', NULL, 0, 0, NULL, 1, 0, N'admin') END";

        //    _context.ExecuteSqlCommand(insertUser);
        //}

        public static void InitialUserAndSecurityModel()
        {
            var securityGroups = new List<SecurityGroup>();

            securityGroups.Add(new SecurityGroup() { GroupName = "Sales" });
            securityGroups.Add(new SecurityGroup() { GroupName = "Purchasing" });
            securityGroups.Add(new SecurityGroup() { GroupName = "Items" });
            securityGroups.Add(new SecurityGroup() { GroupName = "Financials" });
            securityGroups.Add(new SecurityGroup() { GroupName = "Administration" });

            var securityRoles = new List<SecurityRole>();

            securityRoles.Add(new SecurityRole() { RoleName = "Administrators" });
            securityRoles.Add(new SecurityRole() { RoleName = "Users" });

            var user = new User() { Lastname = "System", Firstname = "Administrator", UserName = "admin", EmailAddress = "admin@email.com" };

            _context.SecurityGroups.AddRange(securityGroups);

            _context.SecurityRoles.AddRange(securityRoles);

            _context.Users.Add(user);

            _context.SaveChanges();
        }

        public static Company CreateDefaultCompany()
        {
            var company = new Company()
            {
                Name = "Financial Solutions Inc.",
                CompanyCode = "100",
                ShortName = "FSI",
            };

            _context.Companies.Add(company);
            _context.SaveChanges();

            return company;
        }

        //public static FinancialYear InitFiscalYear()
        //{
        //    _context.FiscalYears.Add(new FinancialYear() { FiscalYearCode = "FY1516", FiscalYearName = "FY 2016/2017", StartDate = new DateTime(2016, 01, 01), EndDate = new DateTime(2016, 12, 31), IsActive = true });
        //    _context.SaveChanges();

        //    return _context.FiscalYears.FirstOrDefault();
        //}

        public static List<PaymentTerm> InitPaymentTerms()
        {
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Payment due within 10 days",
                PaymentType = PaymentTypes.AfterNoOfDays,
                DueAfterDays = 10,
                IsActive = true,
            });
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Due 15th Of the Following Month 	",
                PaymentType = PaymentTypes.DayInTheFollowingMonth,
                DueAfterDays = 15,
                IsActive = true,
            });
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Cash Only",
                PaymentType = PaymentTypes.Cash,
                IsActive = true,
            });
            _context.SaveChanges();

            return _context.PaymentTerms.ToList();
        }

        public static List<AccountClass> InitializeAccountClasses()
        {
            _context.AccountClasses.Add(new AccountClass() { Name = "Assets", NormalBalance = "Dr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Liabilities", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Equity", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Revenue", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Expense", NormalBalance = "Dr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Temporary", NormalBalance = "NA" });

            _context.SaveChanges();

            return _context.AccountClasses.ToList();
        }

        //public static List<Account> LoadChartOfAccountsFromFile(string filename, int companyId)
        //{
        //    DataTable csvData = new DataTable();
        //    using (TextFieldParser csvReader = new TextFieldParser(filename))
        //    {
        //        csvReader.SetDelimiters(new string[] { "," });
        //        csvReader.HasFieldsEnclosedInQuotes = true;
        //        string[] colFields = csvReader.ReadFields();
        //        foreach (string column in colFields)
        //        {
        //            DataColumn datecolumn = new DataColumn(column);
        //            datecolumn.AllowDBNull = true;
        //            csvData.Columns.Add(datecolumn);
        //        }
        //        while (!csvReader.EndOfData)
        //        {
        //            string[] fieldData = csvReader.ReadFields();
        //            for (int i = 0; i < fieldData.Length; i++)
        //            {
        //                if (fieldData[i] == "")
        //                {
        //                    fieldData[i] = null;
        //                }
        //            }
        //            csvData.Rows.Add(fieldData);
        //        }
        //        List<Account> accounts = new List<Account>();
        //        foreach (DataRow row in csvData.Rows)
        //        {
        //            Account account = new Account();
        //            account.AccountCode = row["AccountCode"].ToString();
        //            account.AccountName = row["AccountName"].ToString();
        //            account.AccountClassId = int.Parse(row["AccountClass"].ToString());
        //            account.IsCash = bool.Parse(row["Cash"].ToString());
        //            account.IsContraAccount = bool.Parse(row["ContraAccount"].ToString());

        //            if (row["Sign"].ToString() == "DR")
        //                account.DrOrCrSide = DrOrCrSide.Dr;
        //            else if(row["Sign"].ToString() == "CR")
        //                account.DrOrCrSide = DrOrCrSide.Cr;
        //            else
        //                account.DrOrCrSide = DrOrCrSide.NA;

        //            account.CompanyId = companyId;
        //            accounts.Add(account);
        //        }

        //        _context.Accounts.AddRange(accounts);

        //        try
        //        {
        //            _context.SaveChanges();
        //        }
        //        catch(Exception ex)
        //        {
        //            throw ex;
        //        }

        //        return _context.Accounts.ToList();
        //    }
        //}

        //public static void UpdateAccountsParentCodes(string filename)
        //{
        //    DataTable csvData = new DataTable();
        //    using (TextFieldParser csvReader = new TextFieldParser(filename))
        //    {
        //        csvReader.SetDelimiters(new string[] { "," });
        //        csvReader.HasFieldsEnclosedInQuotes = true;
        //        string[] colFields = csvReader.ReadFields();
        //        foreach (string column in colFields)
        //        {
        //            DataColumn datecolumn = new DataColumn(column);
        //            datecolumn.AllowDBNull = true;
        //            csvData.Columns.Add(datecolumn);
        //        }
        //        while (!csvReader.EndOfData)
        //        {
        //            string[] fieldData = csvReader.ReadFields();
        //            for (int i = 0; i < fieldData.Length; i++)
        //            {
        //                if (fieldData[i] == "")
        //                {
        //                    fieldData[i] = null;
        //                }
        //            }
        //            csvData.Rows.Add(fieldData);
        //        }

        //        foreach (DataRow row in csvData.Rows)
        //        {
        //            string accountCode = row["AccountCode"].ToString();
        //            string parentAccountCode = row["ParentAccountCode"].ToString();

        //            var account = _context.Accounts.Where(a => a.AccountCode == accountCode).FirstOrDefault();
        //            _context.Set<Account>().Attach(account);
        //            var parentAccount = _context.Accounts.Where(a => a.AccountCode == parentAccountCode).FirstOrDefault();
        //            if (parentAccount != null)
        //                account.ParentAccountId = parentAccount.Id;                    
        //        }
        //        var accounts = _context.Accounts.ToList();

        //        try {
        //            _context.SaveChanges();
        //        }
        //        catch(Exception ex)
        //        {
        //            throw ex;
        //        }
        //    }
        //}

        public static GeneralLedgerSetting InitGeneralLedgerSetting()
        {
            var glSetting = new GeneralLedgerSetting()
            {
                Company = _context.Companies.FirstOrDefault(),
                GoodsReceiptNoteClearingAccount = _context.Accounts.Where(a => a.AccountCode == "10810").FirstOrDefault(),
                ShippingChargeAccount = _context.Accounts.Where(a => a.AccountCode == "40500").FirstOrDefault(),
                SalesDiscountAccount = _context.Accounts.Where(a => a.AccountCode == "40400").FirstOrDefault(),
            };
            _context.GeneralLedgerSettings.Add(glSetting);
            _context.SaveChanges();

            return glSetting;
        }

        public static void InitTax()
        {
            // NOTE: each tax should have its own tax account.

            var salesTaxAccount = _context.Accounts.Where(a => a.AccountCode == "20300").FirstOrDefault();
            var purchaseTaxAccount = _context.Accounts.Where(a => a.AccountCode == "50700").FirstOrDefault();

            var vat5 = new Tax()
            {
                TaxCode = "VAT5%",
                TaxName = "VAT 5%",
                Rate = 5,
                IsActive = true,
                SalesAccountId = salesTaxAccount.Id,
                PurchasingAccountId = purchaseTaxAccount.Id,
            };

            var vat10 = new Tax()
            {
                TaxCode = "VAT10%",
                TaxName = "VAT 10%",
                Rate = 10,
                IsActive = true,
                SalesAccountId = salesTaxAccount.Id,
                PurchasingAccountId = purchaseTaxAccount.Id,
            };

            var evat12 = new Tax()
            {
                TaxCode = "VAT12%",
                TaxName = "VAT 12%",
                Rate = 12,
                IsActive = true,
                SalesAccountId = salesTaxAccount.Id,
                PurchasingAccountId = purchaseTaxAccount.Id,
            };

            var exportTax1 = new Tax()
            {
                TaxCode = "exportTax1%",
                TaxName = "Export Tax 1%",
                Rate = 1,
                IsActive = true,
                SalesAccountId = salesTaxAccount.Id,
                PurchasingAccountId = purchaseTaxAccount.Id,
            };

            _context.Taxes.Add(vat5);
            _context.Taxes.Add(vat10);
            _context.Taxes.Add(evat12);
            _context.Taxes.Add(exportTax1);

            var taxGroupVAT = new TaxGroup()
            {
                Description = "VAT",
                TaxAppliedToShipping = false,
                IsActive = true,
            };

            var taxGroupExport = new TaxGroup()
            {
                Description = "Export",
                TaxAppliedToShipping = false,
                IsActive = true,
            };

            _context.TaxGroups.Add(taxGroupVAT);
            _context.TaxGroups.Add(taxGroupExport);

            var itemTaxGroupRegular = new ItemTaxGroup()
            {
                Name = "Regular",
                IsFullyExempt = false,
            };

            var itemTaxGroupRegularPreferenced = new ItemTaxGroup()
            {
                Name = "Preferenced",
                IsFullyExempt = false,
            };

            _context.ItemTaxGroups.Add(itemTaxGroupRegular);
            _context.ItemTaxGroups.Add(itemTaxGroupRegularPreferenced);

            vat5.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupVAT,
            });

            evat12.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupVAT,
            });

            exportTax1.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupExport,
            });

            vat5.ItemTaxGroupTaxes.Add(new ItemTaxGroupTax()
            {
                ItemTaxGroup = itemTaxGroupRegularPreferenced,
                IsExempt = false,
            });

            evat12.ItemTaxGroupTaxes.Add(new ItemTaxGroupTax()
            {
                ItemTaxGroup = itemTaxGroupRegular,
                IsExempt = false,
            });

            _context.SaveChanges();
        }

        public static Vendor InitVendor()
        {
            Party vendorParty = new Party();
            vendorParty.Name = "ABC Sample Supplier";
            vendorParty.PartyType = Core.Domain.PartyTypes.Vendor;
            vendorParty.IsActive = true;

            Vendor vendor = new Vendor();
            vendor.No = "1";
            vendor.AccountsPayableAccountId = _context.Accounts.Where(a => a.AccountName == "Accounts Payable").FirstOrDefault().Id;
            vendor.PurchaseAccountId = _context.Accounts.Where(a => a.AccountName == "Purchase A/C").FirstOrDefault().Id;
            vendor.PurchaseDiscountAccountId = _context.Accounts.Where(a => a.AccountName == "Purchase Discounts").FirstOrDefault().Id;
            vendor.Party = vendorParty;

            Contact primaryContact = new Contact();
            primaryContact.ContactType = ContactTypes.Vendor;
            primaryContact.FirstName = "Mary";
            primaryContact.LastName = "Walter";

            primaryContact.Party = new Party();
            primaryContact.Party.Name = "Mary Walter";
            primaryContact.Party.PartyType = PartyTypes.Contact;

            vendor.PrimaryContact = primaryContact;                        

            _context.Vendors.Add(vendor);

            _context.SaveChanges();

            return vendor;
        }

        public  static Customer InitCustomer()
        {
            var accountAR = _context.Accounts.Where(e => e.AccountCode == "10120").FirstOrDefault();
            var accountSales = _context.Accounts.Where(e => e.AccountCode == "40100").FirstOrDefault();
            var accountAdvances = _context.Accounts.Where(e => e.AccountCode == "20120").FirstOrDefault();
            var accountSalesDiscount = _context.Accounts.Where(e => e.AccountCode == "40400").FirstOrDefault();

            Party customerParty = new Party();
            customerParty.Name = "ABC Customer";
            customerParty.PartyType = Core.Domain.PartyTypes.Customer;
            customerParty.IsActive = true;

            Customer customer = new Customer();
            customer.No = "1";
            customer.AccountsReceivableAccountId = accountAR != null ? (int?)accountAR.Id : null;
            customer.SalesAccountId = accountSales != null ? (int?)accountSales.Id : null;
            customer.CustomerAdvancesAccountId = accountAdvances != null ? (int?)accountAdvances.Id : null;
            customer.SalesDiscountAccountId = accountSalesDiscount != null ? (int?)accountSalesDiscount.Id : null;
            customer.TaxGroupId = _context.TaxGroups.Where(tg => tg.Description == "VAT").FirstOrDefault().Id;
            customer.Party = customerParty;

            Contact primaryContact = new Contact();
            primaryContact.ContactType = ContactTypes.Customer;
            primaryContact.FirstName = "John";
            primaryContact.LastName = "Doe";

            primaryContact.Party = new Party();
            primaryContact.Party.Name = "John Doe";
            primaryContact.Party.PartyType = PartyTypes.Contact;

            customer.PrimaryContact = primaryContact;

            _context.Customers.Add(customer);
            _context.SaveChanges();

            return customer;
        }

        //public static List<Item> InitItems()
        //{
        //    _context.Measurements.Add(new Measurement() { Code = "EA", Description = "Each" });
        //    _context.Measurements.Add(new Measurement() { Code = "PK", Description = "Pack" });
        //    _context.Measurements.Add(new Measurement() { Code = "MO", Description = "Monthly" });
        //    _context.Measurements.Add(new Measurement() { Code = "HR", Description = "Hour" });
        //    _context.SaveChanges();

        //    // Accounts = Sales A/C (40100), Inventory (10800), COGS (50300), Inv Adjustment (50500), Item Assm Cost (10900)
        //    var sales = _context.Accounts.Where(a => a.AccountCode == "40100").FirstOrDefault();
        //    var inventory = _context.Accounts.Where(a => a.AccountCode == "10800").FirstOrDefault();
        //    var invAdjusment = _context.Accounts.Where(a => a.AccountCode == "50500").FirstOrDefault();
        //    var cogs = _context.Accounts.Where(a => a.AccountCode == "50300").FirstOrDefault();
        //    var assemblyCost = _context.Accounts.Where(a => a.AccountCode == "10900").FirstOrDefault();

        //    _context.ItemCategories.Add(new ItemCategory()
        //    {
        //        Name = "Charges",
        //        Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        ItemType = ItemTypes.Charge,
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        AdjustmentAccount = invAdjusment,
        //        CostOfGoodsSoldAccount = cogs,
        //        AssemblyAccount = assemblyCost,
        //    }).Items.Add(new Item()
        //    {
        //        Description = "HOA Dues",
        //        SellDescription = "HOA Dues",
        //        PurchaseDescription = "HOA Dues",
        //        Price = 350,
        //        SmallestMeasurement = _context.Measurements.Where(m => m.Code == "MO").FirstOrDefault(),
        //        SellMeasurement = _context.Measurements.Where(m => m.Code == "MO").FirstOrDefault(),
        //        SalesAccount = _context.Accounts.Where(a => a.AccountCode == "40200").FirstOrDefault(),
        //        No = "1"
        //    });

        //    var componentCategory = new ItemCategory()
        //    {
        //        Name = "Components",
        //        Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        ItemType = ItemTypes.Purchased,
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        AdjustmentAccount = invAdjusment,
        //        CostOfGoodsSoldAccount = cogs,
        //        AssemblyAccount = assemblyCost,
        //    };

        //    var carStickerItem = new Item()
        //    {
        //        Description = "Car Sticker",
        //        SellDescription = "Car Sticker",
        //        PurchaseDescription = "Car Sticker",
        //        Price = 100,
        //        Cost = 40,
        //        SmallestMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        SellMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        PurchaseMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        CostOfGoodsSoldAccount = cogs,
        //        InventoryAdjustmentAccount = invAdjusment,
        //        ItemTaxGroup = _context.ItemTaxGroups.Where(m => m.Name == "Regular").FirstOrDefault(),
        //        No = "2"
        //    };

        //    var otherItem = new Item()
        //    {
        //        Description = "Optical Mouse",
        //        SellDescription = "Optical Mouse",
        //        PurchaseDescription = "Optical Mouse",
        //        Price = 80,
        //        Cost = 30,
        //        SmallestMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        SellMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        PurchaseMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        CostOfGoodsSoldAccount = cogs,
        //        InventoryAdjustmentAccount = invAdjusment,
        //        ItemTaxGroup = _context.ItemTaxGroups.Where(m => m.Name == "Regular").FirstOrDefault(),
        //        No = "3"
        //    };

        //    componentCategory.Items.Add(carStickerItem);
        //    componentCategory.Items.Add(otherItem);
        //    _context.ItemCategories.Add(componentCategory);

        //    _context.ItemCategories.Add(new ItemCategory()
        //    {
        //        Name = "Services",
        //        Measurement = _context.Measurements.Where(m => m.Code == "HR").FirstOrDefault(),
        //        ItemType = ItemTypes.Service,
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        AdjustmentAccount = invAdjusment,
        //        CostOfGoodsSoldAccount = cogs,
        //        AssemblyAccount = assemblyCost,
        //    });

        //    _context.ItemCategories.Add(new ItemCategory()
        //    {
        //        Name = "Systems",
        //        Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
        //        ItemType = ItemTypes.Manufactured,
        //        SalesAccount = sales,
        //        InventoryAccount = inventory,
        //        AdjustmentAccount = invAdjusment,
        //        CostOfGoodsSoldAccount = cogs,
        //        AssemblyAccount = assemblyCost,
        //    });

        //    _context.SaveChanges();

        //    return _context.Items.ToList();
        //}

        public static List<Bank> InitBanks()
        {
            var bank = new Bank()
            {
                AccountId = _context.Accounts.Where(a => a.AccountCode == "10111").FirstOrDefault().Id,
                Name = "General Fund",
                Type = BankTypes.CheckingAccount,
                BankName = "GFB",
                Number = "1234567890",
                Address = "123 Main St.",
                IsDefault = true,
                IsActive = true,
            };
            _context.Banks.Add(bank);

            bank = new Bank()
            {
                AccountId = _context.Accounts.Where(a => a.AccountCode == "10113").FirstOrDefault().Id,
                Name = "Petty Cash Account",
                Type = BankTypes.CashAccount,
                IsDefault = false,
                IsActive = true,
            };
            _context.Banks.Add(bank);
            _context.SaveChanges();

            return _context.Banks.ToList();
        }

        public static void InitializeEntityToAudit()
        {
            var auditAccount = new AuditableEntity();
            auditAccount.EntityName = "Account";
            auditAccount.EnableAudit = true;

            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "CompanyId", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "AccountClassId", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "ParentAccountId", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "DrOrCrSide", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "AccountCode", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "AccountName", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "Description", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "IsCash", EnableAudit = true });
            auditAccount.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "IsContraAccount", EnableAudit = true });

            _context.AuditableEntities.Add(auditAccount);

            var auditJE = new AuditableEntity();
            auditJE.EntityName = "JournalEntryHeader";
            auditJE.EnableAudit = true;

            auditJE.AuditableAttributes.Add(new AuditableAttribute() { AttributeName = "Posted", EnableAudit = true });

            _context.AuditableEntities.Add(auditJE);

            _context.SaveChanges();
        }
    }
}

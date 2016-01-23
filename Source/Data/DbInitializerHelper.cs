using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Data
{
    public static class DbInitializerHelper
    {
        public static ApplicationContext _context;
        public static string _filename;

        public static void Initialize()
        {
            try
            {
                if (_context == null)
                    _context = new ApplicationContext();
                if (string.IsNullOrEmpty(_filename))
                    _filename = AppDomain.CurrentDomain.BaseDirectory + "/App_Data/coa.csv";

                DbInitializerHelper.InsertAdminUser();

                if (_context.Users.Count() == 0)
                    DbInitializerHelper.InitialUserAndSecurityModel();

                Company company = null;
                if (_context.Companies.Count() == 0)
                    company = DbInitializerHelper.CreateDefaultCompany();
                else
                    company = _context.Companies.FirstOrDefault();

                FiscalYear fy = null;
                if (_context.FiscalYears.Count() == 0)
                    fy = InitFiscalYear();

                List<PaymentTerm> paymentTerms = null;
                if (_context.PaymentTerms.Count() == 0)
                    paymentTerms = InitPaymentTerms();

                if (_context.AccountClasses.Count() == 0)
                    DbInitializerHelper.InitializeAccountClasses();

                if (_context.Accounts.Count() == 0)
                {
                    DbInitializerHelper.LoadChartOfAccountsFromFile(_filename, company.Id);
                    DbInitializerHelper.UpdateAccountsParentCodes(_filename);
                }

                GeneralLedgerSetting glSetting = null;
                if (_context.GeneralLedgerSettings.Count() == 0)
                    glSetting = InitGeneralLedgerSetting();

                if (_context.Taxes.Count() == 0)
                    DbInitializerHelper.InitTax();

                Vendor vendor = null;
                if (_context.Vendors.Count() == 0)
                    vendor = DbInitializerHelper.InitVendor();

                Customer customer = null;
                if (_context.Customers.Count() == 0)
                    customer = DbInitializerHelper.InitCustomer();

                List<Item> items = null;
                if (_context.Items.Count() == 0)
                    items = InitItems();

                List<Bank> banks = null;
                if (_context.Banks.Count() == 0)
                    banks = InitBanks();

                
            }
            catch
            {
            }
            finally
            {
                _context.Dispose();
            }
        }

        public static void InsertAdminUser()
        {
            string insertUser = "IF NOT EXISTS (SELECT 1 FROM [dbo].[AspNetUsers]) BEGIN INSERT[dbo].[AspNetUsers]([Id], [Email], [EmailConfirmed], [PasswordHash], [SecurityStamp], [PhoneNumber], [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEndDateUtc], [LockoutEnabled], [AccessFailedCount], [UserName])";
            insertUser += "VALUES(N'c12b7ec4-72ab-4960-918a-1a397b15dfd7', N'admin@email.com', 0, N'AMAeZqmeyoa5vcvxJ3f3B6YEgLImAbzrlleAXKX2zer/C0TVx0J6gjzyo47yU5YwGw==', N'8b035264-31e2-4b8f-8ea1-2c608debcd14', NULL, 0, 0, NULL, 1, 0, N'admin') END";

            _context.ExecuteSqlCommand(insertUser);
        }

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
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            _context.Companies.Add(company);
            _context.SaveChanges();

            return company;
        }

        public static FiscalYear InitFiscalYear()
        {
            _context.FiscalYears.Add(new FiscalYear() { FiscalYearCode = "FY1516", FiscalYearName = "FY 2016/2017", StartDate = new DateTime(2016, 01, 01), EndDate = new DateTime(2016, 12, 31), IsActive = true });
            _context.SaveChanges();

            return _context.FiscalYears.FirstOrDefault();
        }

        public static List<PaymentTerm> InitPaymentTerms()
        {
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Payment due within 10 days",
                PaymentType = PaymentTypes.AfterNoOfDays,
                DueAfterDays = 10,
                IsActive = true,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Due 15th Of the Following Month 	",
                PaymentType = PaymentTypes.DayInTheFollowingMonth,
                DueAfterDays = 15,
                IsActive = true,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });
            _context.PaymentTerms.Add(new PaymentTerm()
            {
                Description = "Cash Only",
                PaymentType = PaymentTypes.Cash,
                IsActive = true,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
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

            _context.SaveChanges();

            return _context.AccountClasses.ToList();
        }

        public static List<Account> LoadChartOfAccountsFromFile(string filename, int companyId)
        {
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(filename))
            {
                csvReader.SetDelimiters(new string[] { "," });
                csvReader.HasFieldsEnclosedInQuotes = true;
                string[] colFields = csvReader.ReadFields();
                foreach (string column in colFields)
                {
                    DataColumn datecolumn = new DataColumn(column);
                    datecolumn.AllowDBNull = true;
                    csvData.Columns.Add(datecolumn);
                }
                while (!csvReader.EndOfData)
                {
                    string[] fieldData = csvReader.ReadFields();
                    for (int i = 0; i < fieldData.Length; i++)
                    {
                        if (fieldData[i] == "")
                        {
                            fieldData[i] = null;
                        }
                    }
                    csvData.Rows.Add(fieldData);
                }
                List<Account> accounts = new List<Account>();
                foreach (DataRow row in csvData.Rows)
                {
                    Account account = new Account();
                    account.AccountCode = row["AccountCode"].ToString();
                    account.AccountName = row["AccountName"].ToString();
                    account.AccountClassId = int.Parse(row["AccountClass"].ToString());
                    account.IsCash = bool.Parse(row["Cash"].ToString());
                    account.IsContraAccount = bool.Parse(row["ContraAccount"].ToString());
                    account.TransactionType = row["Sign"].ToString() == "DR" ? TransactionTypes.Dr : TransactionTypes.Cr;
                    account.CompanyId = companyId;

                    account.CreatedBy = "System";
                    account.CreatedOn = DateTime.Now;
                    account.ModifiedBy = "System";
                    account.ModifiedOn = DateTime.Now;
                    accounts.Add(account);
                }

                _context.Accounts.AddRange(accounts);
                _context.SaveChanges();

                return _context.Accounts.ToList();
            }
        }

        public static void UpdateAccountsParentCodes(string filename)
        {
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(filename))
            {
                csvReader.SetDelimiters(new string[] { "," });
                csvReader.HasFieldsEnclosedInQuotes = true;
                string[] colFields = csvReader.ReadFields();
                foreach (string column in colFields)
                {
                    DataColumn datecolumn = new DataColumn(column);
                    datecolumn.AllowDBNull = true;
                    csvData.Columns.Add(datecolumn);
                }
                while (!csvReader.EndOfData)
                {
                    string[] fieldData = csvReader.ReadFields();
                    for (int i = 0; i < fieldData.Length; i++)
                    {
                        if (fieldData[i] == "")
                        {
                            fieldData[i] = null;
                        }
                    }
                    csvData.Rows.Add(fieldData);
                }

                foreach (DataRow row in csvData.Rows)
                {
                    string accountCode = row["AccountCode"].ToString();
                    string parentAccountCode = row["ParentAccountCode"].ToString();

                    var account = _context.Accounts.Where(a => a.AccountCode == accountCode).FirstOrDefault();
                    _context.Set<Account>().Attach(account);
                    var parentAccount = _context.Accounts.Where(a => a.AccountCode == parentAccountCode).FirstOrDefault();
                    if (parentAccount != null)
                        account.ParentAccountId = parentAccount.Id;
                    _context.SaveChanges();
                }
                var accounts = _context.Accounts.ToList();
                foreach (var account in accounts)
                {
                    var acc = _context.Accounts.Where(a => a.AccountCode == account.AccountCode).FirstOrDefault();
                    _context.Set<Account>().Attach(acc);
                    if (acc.ChildAccounts != null && acc.ChildAccounts.Count > 0)
                        acc.AccountType = Core.Domain.AccountTypes.Heading;
                    else
                        acc.AccountType = Core.Domain.AccountTypes.Posting;
                    _context.SaveChanges();
                }
            }
        }

        public static GeneralLedgerSetting InitGeneralLedgerSetting()
        {
            var glSetting = new GeneralLedgerSetting()
            {
                Company = _context.Companies.FirstOrDefault(),
                GoodsReceiptNoteClearingAccount = _context.Accounts.Where(a => a.AccountCode == "10810").FirstOrDefault(),
                ShippingChargeAccount = _context.Accounts.Where(a => a.AccountCode == "40500").FirstOrDefault(),
                SalesDiscountAccount = _context.Accounts.Where(a => a.AccountCode == "40400").FirstOrDefault(),
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };
            _context.GeneralLedgerSettings.Add(glSetting);
            _context.SaveChanges();

            return glSetting;
        }

        public static void InitTax()
        {
            var vat5 = new Tax()
            {
                TaxCode = "VAT5%",
                TaxName = "VAT 5%",
                Rate = 5,
                IsActive = true,
                SalesAccountId = 37,
                PurchasingAccountId = 37,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var vat10 = new Tax()
            {
                TaxCode = "VAT10%",
                TaxName = "VAT 10%",
                Rate = 10,
                IsActive = true,
                SalesAccountId = 37,
                PurchasingAccountId = 37,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var evat12 = new Tax()
            {
                TaxCode = "VAT12%",
                TaxName = "VAT 12%",
                Rate = 12,
                IsActive = true,
                SalesAccountId = 37,
                PurchasingAccountId = 37,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var exportTax1 = new Tax()
            {
                TaxCode = "exportTax1%",
                TaxName = "Export Tax 1%",
                Rate = 1,
                IsActive = true,
                SalesAccountId = 37,
                PurchasingAccountId = 37,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
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
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var taxGroupExport = new TaxGroup()
            {
                Description = "Export",
                TaxAppliedToShipping = false,
                IsActive = true,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            _context.TaxGroups.Add(taxGroupVAT);
            _context.TaxGroups.Add(taxGroupExport);

            var itemTaxGroupRegular = new ItemTaxGroup()
            {
                Name = "Regular",
                IsFullyExempt = false,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var itemTaxGroupRegularPreferenced = new ItemTaxGroup()
            {
                Name = "Preferenced",
                IsFullyExempt = false,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            _context.ItemTaxGroups.Add(itemTaxGroupRegular);
            _context.ItemTaxGroups.Add(itemTaxGroupRegularPreferenced);

            vat5.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupVAT,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            evat12.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupVAT,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            exportTax1.TaxGroupTaxes.Add(new TaxGroupTax()
            {
                TaxGroup = taxGroupExport,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            vat5.ItemTaxGroupTaxes.Add(new ItemTaxGroupTax()
            {
                ItemTaxGroup = itemTaxGroupRegular,
                IsExempt = false,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            evat12.ItemTaxGroupTaxes.Add(new ItemTaxGroupTax()
            {
                ItemTaxGroup = itemTaxGroupRegularPreferenced,
                IsExempt = false,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            _context.SaveChanges();
        }

        public static Vendor InitVendor()
        {
            Vendor vendor = new Vendor();
            vendor.No = "1";
            vendor.Name = "ABC Sample Supplier";
            vendor.AccountsPayableAccountId = _context.Accounts.Where(a => a.AccountName == "Accounts Payable").FirstOrDefault().Id;
            vendor.PurchaseAccountId = _context.Accounts.Where(a => a.AccountName == "Purchase A/C").FirstOrDefault().Id;
            vendor.PurchaseDiscountAccountId = _context.Accounts.Where(a => a.AccountName == "Purchase Discounts").FirstOrDefault().Id;
            vendor.PartyType = Core.Domain.PartyTypes.Vendor;
            vendor.CreatedBy = "System";
            vendor.CreatedOn = DateTime.Now;
            vendor.ModifiedBy = "System";
            vendor.ModifiedOn = DateTime.Now;

            Contact primaryContact = new Contact();
            primaryContact.ContactType = ContactTypes.Vendor;
            primaryContact.PartyType = PartyTypes.Contact;
            primaryContact.FirstName = "Mary";
            primaryContact.LastName = "Walter";
            primaryContact.CreatedBy = "System";
            primaryContact.CreatedOn = DateTime.Now;
            primaryContact.ModifiedBy = "System";
            primaryContact.ModifiedOn = DateTime.Now;
            primaryContact.Party = vendor;

            vendor.PrimaryContact = primaryContact;

            _context.Vendors.Add(vendor);

            return vendor;
        }

        public  static Customer InitCustomer()
        {
            var accountAR = _context.Accounts.Where(e => e.AccountCode == "10120").FirstOrDefault();

            Customer customer = new Customer();
            customer.No = "1";
            customer.IsActive = true;
            customer.PartyType = Core.Domain.PartyTypes.Customer;
            customer.Name = "ABC Customer";
            customer.AccountsReceivableAccountId = accountAR != null ? (int?)accountAR.Id : null;
            customer.CreatedBy = "System";
            customer.CreatedOn = DateTime.Now;
            customer.ModifiedBy = "System";
            customer.ModifiedOn = DateTime.Now;

            Contact primaryContact = new Contact();
            primaryContact.ContactType = ContactTypes.Customer;
            primaryContact.PartyType = PartyTypes.Contact;
            primaryContact.FirstName = "John";
            primaryContact.LastName = "Doe";
            primaryContact.CreatedBy = "System";
            primaryContact.CreatedOn = DateTime.Now;
            primaryContact.ModifiedBy = "System";
            primaryContact.ModifiedOn = DateTime.Now;
            primaryContact.Party = customer;

            customer.PrimaryContact = primaryContact;

            _context.Customers.Add(customer);
            _context.SaveChanges();

            return customer;
        }

        public static List<Item> InitItems()
        {
            _context.Measurements.Add(new Measurement() { Code = "EA", Description = "Each" });
            _context.Measurements.Add(new Measurement() { Code = "PK", Description = "Pack" });
            _context.Measurements.Add(new Measurement() { Code = "MO", Description = "Monthly" });
            _context.Measurements.Add(new Measurement() { Code = "HR", Description = "Hour" });
            _context.SaveChanges();

            // Accounts = Sales A/C (40100), Inventory (10800), COGS (50300), Inv Adjustment (50500), Item Assm Cost (10900)
            var sales = _context.Accounts.Where(a => a.AccountCode == "40100").FirstOrDefault();
            var inventory = _context.Accounts.Where(a => a.AccountCode == "10800").FirstOrDefault();
            var invAdjusment = _context.Accounts.Where(a => a.AccountCode == "50500").FirstOrDefault();
            var cogs = _context.Accounts.Where(a => a.AccountCode == "50300").FirstOrDefault();
            var assemblyCost = _context.Accounts.Where(a => a.AccountCode == "10900").FirstOrDefault();

            _context.ItemCategories.Add(new ItemCategory()
            {
                Name = "Charges",
                Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                ItemType = ItemTypes.Charge,
                SalesAccount = sales,
                InventoryAccount = inventory,
                AdjustmentAccount = invAdjusment,
                CostOfGoodsSoldAccount = cogs,
                AssemblyAccount = assemblyCost,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now,
            }).Items.Add(new Item()
            {
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now,
                Description = "HOA Dues",
                SellDescription = "HOA Dues",
                PurchaseDescription = "HOA Dues",
                Price = 350,
                SmallestMeasurement = _context.Measurements.Where(m => m.Code == "MO").FirstOrDefault(),
                SellMeasurement = _context.Measurements.Where(m => m.Code == "MO").FirstOrDefault(),
                SalesAccount = _context.Accounts.Where(a => a.AccountCode == "40200").FirstOrDefault(),
                No = "1"
            });

            var componentCategory = new ItemCategory()
            {
                Name = "Components",
                Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                ItemType = ItemTypes.Purchased,
                SalesAccount = sales,
                InventoryAccount = inventory,
                AdjustmentAccount = invAdjusment,
                CostOfGoodsSoldAccount = cogs,
                AssemblyAccount = assemblyCost,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            var carStickerItem = new Item()
            {
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now,
                Description = "Car Sticker",
                SellDescription = "Car Sticker",
                PurchaseDescription = "Car Sticker",
                Price = 100,
                Cost = 40,
                SmallestMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                SellMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                PurchaseMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                SalesAccount = sales,
                InventoryAccount = inventory,
                CostOfGoodsSoldAccount = cogs,
                InventoryAdjustmentAccount = invAdjusment,
                ItemTaxGroup = _context.ItemTaxGroups.Where(m => m.Name == "Regular").FirstOrDefault(),
                No = "2"
            };

            var otherItem = new Item()
            {
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now,
                Description = "Optical Mouse",
                SellDescription = "Optical Mouse",
                PurchaseDescription = "Optical Mouse",
                Price = 80,
                Cost = 30,
                SmallestMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                SellMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                PurchaseMeasurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                SalesAccount = sales,
                InventoryAccount = inventory,
                CostOfGoodsSoldAccount = cogs,
                InventoryAdjustmentAccount = invAdjusment,
                ItemTaxGroup = _context.ItemTaxGroups.Where(m => m.Name == "Regular").FirstOrDefault(),
                No = "3"
            };

            componentCategory.Items.Add(carStickerItem);
            componentCategory.Items.Add(otherItem);
            _context.ItemCategories.Add(componentCategory);

            _context.ItemCategories.Add(new ItemCategory()
            {
                Name = "Services",
                Measurement = _context.Measurements.Where(m => m.Code == "HR").FirstOrDefault(),
                ItemType = ItemTypes.Service,
                SalesAccount = sales,
                InventoryAccount = inventory,
                AdjustmentAccount = invAdjusment,
                CostOfGoodsSoldAccount = cogs,
                AssemblyAccount = assemblyCost,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            _context.ItemCategories.Add(new ItemCategory()
            {
                Name = "Systems",
                Measurement = _context.Measurements.Where(m => m.Code == "EA").FirstOrDefault(),
                ItemType = ItemTypes.Manufactured,
                SalesAccount = sales,
                InventoryAccount = inventory,
                AdjustmentAccount = invAdjusment,
                CostOfGoodsSoldAccount = cogs,
                AssemblyAccount = assemblyCost,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            });

            _context.SaveChanges();

            return _context.Items.ToList();
        }

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
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };
            _context.Banks.Add(bank);

            bank = new Bank()
            {
                AccountId = _context.Accounts.Where(a => a.AccountCode == "10113").FirstOrDefault().Id,
                Name = "Petty Cash Account",
                Type = BankTypes.CashAccount,
                IsDefault = false,
                IsActive = true,
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };
            _context.Banks.Add(bank);
            _context.SaveChanges();

            return _context.Banks.ToList();
        }
    }
}

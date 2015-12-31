//-----------------------------------------------------------------------
// <copyright file="DbInitializer.cs" company="AccountGo">
// Copyright (c) AccountGo. All rights reserved.
// <author>Marvin Perez</author>
// <date>1/11/2015 9:50:13 AM</date>
// </copyright>
//-----------------------------------------------------------------------

using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Data;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Transactions;

namespace Data
{
    public class DbInitializer<TContext> : IDatabaseInitializer<TContext> where TContext : ApplicationContext, new()
    {
        protected ApplicationContext _context;
        protected const string Sql =
            "if (select DB_ID('{0}')) is not null "
            + "begin "
            + "alter database [{0}] set offline with rollback immediate; "
            + "alter database [{0}] set online; "
            + "drop database [{0}]; "
            + "end";

        public virtual void InitializeDatabase(TContext context)
        {
            if (DbExists(context))
            {
                if (context.Database.CompatibleWithModel(false))
                    return;
                DropDatabase(context);
            }
            context.Database.Create();
            Seed(context);
            context.SaveChanges();
        }

        protected virtual bool DbExists(TContext context)
        {
            using (new TransactionScope(TransactionScopeOption.Suppress))
            {
                return context.Database.Exists();
            }
        }

        protected virtual void DropDatabase(TContext context)
        {
            context.Database.ExecuteSqlCommand(TransactionalBehavior.DoNotEnsureTransaction, string.Format(Sql, context.Database.Connection.Database));
        }

        protected virtual void Seed(TContext context)
        {
            _context = context;
            InitData();
        }

        private void InitData()
        {
            InitCompany();
            InitAccounts();
            InitGeneralLedgerSetting();
            InitTax();
            //InitCustomers();
            InitItems();
            InitCustomer();
            InitVendor();
            InitFiscalYear();
            InitPaymentTerms();
            InitBanks();
        }

        private void InitUser()
        {
            //var result = await UserManager.CreateAsync("admin", "P@ssw0rd!");
        }

        private void InitBanks()
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
        }

        private void InitPaymentTerms()
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
        }

        private void InitCompany()
        {
            var company = new Company()
            {
                Name = "Financial Solutions Inc.",
                ShortName = "FSI",
                CreatedBy = "System",
                CreatedOn = DateTime.Now,
                ModifiedBy = "System",
                ModifiedOn = DateTime.Now
            };

            _context.Companies.Add(company);
            _context.SaveChanges();
        }

        private void InitFiscalYear()
        {
            _context.FiscalYears.Add(new FiscalYear() { FiscalYearCode = "FY1516", FiscalYearName = "FY 2015/2016", StartDate = new DateTime(2015, 01, 01), EndDate = new DateTime(2015, 12, 31), IsActive = true});
            _context.SaveChanges();
        }

        private void InitGeneralLedgerSetting()
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
        }

        private void InitAccounts()
        {
            _context.AccountClasses.Add(new AccountClass() { Name = "Assets", NormalBalance = "Dr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Liabilities", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Equity", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Revenue", NormalBalance = "Cr" });
            _context.AccountClasses.Add(new AccountClass() { Name = "Expense", NormalBalance = "Dr" });
            _context.SaveChanges();

            string path = AppDomain.CurrentDomain.BaseDirectory + "/App_Data/coa.csv"; 
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(path))
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
                    Account account = new Account();
                    account.AccountCode = row[0].ToString();
                    account.AccountName = row[1].ToString();
                    account.AccountClassId = int.Parse(row[3].ToString());
                    account.CreatedBy = "System";
                    account.CreatedOn = DateTime.Now;
                    account.ModifiedBy = "System";
                    account.ModifiedOn = DateTime.Now;
                    if (account.AccountCode == "10113")
                        account.IsCash = true;
                    _context.Accounts.Add(account);
                }
                _context.SaveChanges();
                UpdateAccounts();
            }
        }

        private void UpdateAccounts()
        {
            string path = AppDomain.CurrentDomain.BaseDirectory + "/App_Data/coa.csv";
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(path))
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
                    string accountCode = row[0].ToString();
                    string parentAccountCode = row[2].ToString();

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

        private void InitTax()
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

        private void InitItems()
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
        }

        private void InitVendor()
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
        }

        private void InitCustomer()
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
        }

        private void InitCustomers()
        {
            DataTable csvData = new DataTable();
            using (TextFieldParser csvReader = new TextFieldParser(@"C:\Development\Practice\apphb\Solution\src\UnitTests\App_Data\homeowners.csv"))
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
            }

            var accountAR = _context.Accounts.Where(e => e.AccountCode == "10120").FirstOrDefault();
            foreach (DataRow row in csvData.Rows)
            {
                Customer customer = new Customer();
                customer.IsActive = true;
                customer.PartyType = Core.Domain.PartyTypes.Customer;
                //customer.FirstName = row[1].ToString();
                //customer.LastName = row[2].ToString();
                //customer.Name = row[0].ToString() + " " + customer.FirstName + " " + customer.LastName;
                customer.AccountsReceivableAccountId = accountAR != null ? (int?)accountAR.Id : null;
                customer.CreatedBy = "System";
                customer.CreatedOn = DateTime.Now;
                customer.ModifiedBy = "System";
                customer.ModifiedOn = DateTime.Now;
                _context.Customers.Add(customer);
            }
            _context.SaveChanges();
        }
    }
}

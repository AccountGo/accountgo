using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Api.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Api.Service
{
    /// <summary>Creates the operational defaults of a workspace without sample transactions.</summary>
    public class CompanyProvisioningService
    {
        private readonly ApiDbContext _db;
        private readonly ITenantContext _tenant;

        public CompanyProvisioningService(ApiDbContext db, ITenantContext tenant)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _tenant = tenant ?? throw new ArgumentNullException(nameof(tenant));
        }

        public bool SlugExists(string slug)
        {
            // Only the global slug namespace is queried; deleted workspaces keep their URLs reserved.
            return _db.Companies.IgnoreQueryFilters().Any(c => c.Slug == slug);
        }

        /// <summary>Seeds missing defaults. The caller owns the transaction.</summary>
        public void Provision(Company company, string currency = "USD")
        {
            using (_tenant.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner))
            {
                var accounts = _db.Accounts.ToDictionary(a => a.AccountCode, StringComparer.Ordinal);
                var templates = Initializer.GetAccountClassesFromCsv();
                var parents = new Dictionary<Account, string>();
                foreach (var template in templates)
                {
                    var accountClass = _db.AccountClasses.FirstOrDefault(c => c.Name == template.Name);
                    if (accountClass == null)
                    {
                        accountClass = new AccountClass { Name = template.Name, NormalBalance = template.NormalBalance };
                        _db.AccountClasses.Add(accountClass);
                    }
                    foreach (var account in template.Accounts)
                    {
                        if (accounts.ContainsKey(account.AccountCode))
                            continue;
                        var parentCode = account.ParentAccount == null ? null : account.ParentAccount.AccountCode;
                        account.ParentAccount = null;
                        account.ParentAccountId = null;
                        account.AccountClassId = accountClass.Id;
                        account.AccountClass = accountClass;
                        account.CompanyId = company.Id;
                        _db.Accounts.Add(account);
                        accounts.Add(account.AccountCode, account);
                        if (parentCode != null)
                            parents.Add(account, parentCode);
                    }
                }
                foreach (var pair in parents)
                    pair.Key.ParentAccount = accounts[pair.Value];
                _db.SaveChanges();

                if (!_db.GeneralLedgerSettings.Any())
                    _db.GeneralLedgerSettings.Add(new GeneralLedgerSetting
                    {
                        CompanyId = company.Id,
                        PayableAccountId = accounts["20110"].Id,
                        GoodsReceiptNoteClearingAccountId = accounts["10810"].Id,
                        SalesDiscountAccountId = accounts["40400"].Id,
                        ShippingChargeAccountId = accounts["40500"].Id
                    });

                var year = DateTime.UtcNow.Year;
                var code = "FY" + year.ToString(CultureInfo.InvariantCulture);
                if (!_db.FinancialYears.Any(f => f.FiscalYearCode == code))
                    _db.FinancialYears.Add(new FinancialYear
                    {
                        CompanyId = company.Id, FiscalYearCode = code, FiscalYearName = code,
                        StartDate = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        EndDate = new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1),
                        IsActive = true
                    });

                foreach (SequenceNumberTypes type in Enum.GetValues(typeof(SequenceNumberTypes)))
                    if (!_db.SequenceNumbers.Any(s => s.SequenceNumberType == type))
                        _db.SequenceNumbers.Add(new SequenceNumber
                        {
                            CompanyId = company.Id, SequenceNumberType = type, Description = type.ToString(),
                            Prefix = "", UsePrefix = false, NextNumber = 1
                        });

                foreach (var days in new[] { 10, 20, 30, 45, 60 })
                    if (!_db.PaymentTerms.Any(p => p.DueAfterDays == days && p.PaymentType == PaymentTypes.AfterNoOfDays))
                        _db.PaymentTerms.Add(new PaymentTerm
                        {
                            CompanyId = company.Id, DueAfterDays = days, PaymentType = PaymentTypes.AfterNoOfDays,
                            Description = $"Payment due within {days} days", IsActive = true
                        });

                var units = new Dictionary<string, string> { { "EA", "Each" }, { "PK", "Pack" }, { "MO", "Monthly" }, { "HR", "Hour" } };
                foreach (var unit in units)
                    if (!_db.Measurements.Any(m => m.Code == unit.Key))
                        _db.Measurements.Add(new Measurement { CompanyId = company.Id, Code = unit.Key, Description = unit.Value });

                if (!_db.CompanySettings.Any())
                    _db.CompanySettings.Add(new CompanySetting { CompanyId = company.Id, Currency = currency });

                // A neutral default avoids imposing another jurisdiction's tax rates on a new workspace.
                if (!_db.TaxGroups.Any())
                {
                    var group = new TaxGroup { CompanyId = company.Id, Description = "No tax", IsActive = true };
                    var tax = new Tax
                    {
                        CompanyId = company.Id, TaxCode = "NONE", TaxName = "No tax", Rate = 0, IsActive = true,
                        SalesAccountId = accounts["20300"].Id, PurchasingAccountId = accounts["50700"].Id
                    };
                    tax.TaxGroupTaxes.Add(new TaxGroupTax { CompanyId = company.Id, TaxGroup = group });
                    _db.Taxes.Add(tax);
                }
                _db.SaveChanges();
            }
        }
    }
}

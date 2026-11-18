// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Api.Data;
using Api.Data.Repositories;
using Api.Data.Schema;
using Api.Service;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class SupplierCreditsTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId) => CompanyId = companyId;
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId => "accountant-1";
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        [Fact]
        public void S01_StandaloneSupplierCredit330_Applies110And165ToTwoBills_Leaves55Available()
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
            var tenant = new FixedTenantContext(1);
            using var db = new ApiDbContext(options, tenant);
            db.Database.EnsureCreated();
            SqliteSchemaUpgrader.Upgrade(conn);

            var company = db.Companies.FirstOrDefault(c => c.Id == 1) ?? new Company { Name = "Co", CompanyCode = "CO", Slug = "co" };
            if (company.Id == 0) { db.Companies.Add(company); db.SaveChanges(); }

            var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id) ?? new CompanySetting { CompanyId = company.Id };
            setting.Currency = "USD";
            setting.CreditsReturnsRefundsEnabled = true;
            setting.BooksStartDate = new DateTime(2026, 1, 1);
            if (setting.Id == 0) db.CompanySettings.Add(setting);
            db.SaveChanges();

            db.FinancialYears.Add(new FinancialYear { CompanyId = company.Id, FiscalYearCode = "2026", FiscalYearName = "FY 2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
            db.AccountClasses.AddRange(
                new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                new AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                new AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
            db.SaveChanges();

            Account AddAcc(string code, string name, AccountClasses cls)
            {
                var acc = new Account { CompanyId = company.Id, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr };
                db.Accounts.Add(acc);
                db.SaveChanges();
                return acc;
            }

            var ap = AddAcc("2000", "Accounts Payable", AccountClasses.Liabilities);
            var suppCred = AddAcc("1310", "Supplier Credits Receivable", AccountClasses.Assets);
            var exp = AddAcc("5000", "Office Supplies", AccountClasses.Expense);
            var inTax = AddAcc("1150", "Input Tax Control", AccountClasses.Assets);

            db.GeneralLedgerSettings.Add(new GeneralLedgerSetting { CompanyId = company.Id, PayableAccountId = ap.Id, SupplierCreditAssetAccountId = suppCred.Id });
            var tax = new Tax { CompanyId = company.Id, TaxCode = "VAT10", TaxName = "VAT 10%", Rate = 10m, PurchasingAccountId = inTax.Id, SalesAccountId = inTax.Id, IsActive = true };
            db.Taxes.Add(tax);
            var item = new Item { CompanyId = company.Id, Code = "SUP", Description = "Supplies" };
            db.Items.Add(item);
            var party = new Party { CompanyId = company.Id, Name = "Vendor A", PartyType = PartyTypes.Vendor };
            db.Parties.Add(party);
            db.SaveChanges();

            var vendor = new Core.Domain.Purchases.Vendor { CompanyId = company.Id, PartyId = party.Id, AccountsPayableAccountId = ap.Id };
            db.Vendors.Add(vendor);
            db.SaveChanges();

            PurchaseInvoiceHeader CreateBill(string no, decimal amt)
            {
                var gl = new GeneralLedgerHeader { CompanyId = company.Id, Date = new DateTime(2026, 11, 1), DocumentType = DocumentTypes.PurchaseInvoice, Description = no };
                gl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = company.Id, AccountId = exp.Id, DrCr = DrOrCrSide.Dr, Amount = amt });
                gl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = company.Id, AccountId = ap.Id, DrCr = DrOrCrSide.Cr, Amount = amt });
                db.GeneralLedgerHeaders.Add(gl);
                db.SaveChanges();

                var bill = new PurchaseInvoiceHeader
                {
                    CompanyId = company.Id,
                    VendorId = vendor.Id,
                    No = no,
                    VendorInvoiceNo = no,
                    Date = new DateTime(2026, 11, 1),
                    Status = PurchaseInvoiceStatus.Open,
                    PayableAccountId = ap.Id,
                    GeneralLedgerHeaderId = gl.Id
                };
                bill.PurchaseInvoiceLines.Add(new PurchaseInvoiceLine { CompanyId = company.Id, ItemId = item.Id, AccountId = exp.Id, Quantity = 1m, Amount = amt });
                db.PurchaseInvoiceHeaders.Add(bill);
                db.SaveChanges();
                return bill;
            }

            var bill1 = CreateBill("BILL-1", 110m);
            var bill2 = CreateBill("BILL-2", 200m);

            var finService = new Services.Financial.FinancialService(
                null!, null!,
                new EfRepository<GeneralLedgerHeader>(NullLogger<GeneralLedgerHeader>.Instance, db),
                new EfRepository<GeneralLedgerLine>(NullLogger<GeneralLedgerLine>.Instance, db),
                new EfRepository<Account>(NullLogger<Account>.Instance, db),
                new EfRepository<AccountClass>(NullLogger<AccountClass>.Instance, db),
                new EfRepository<Tax>(NullLogger<Tax>.Instance, db),
                new EfRepository<JournalEntryHeader>(NullLogger<JournalEntryHeader>.Instance, db),
                new EfRepository<JournalEntryLine>(NullLogger<JournalEntryLine>.Instance, db),
                new EfRepository<FinancialYear>(NullLogger<FinancialYear>.Instance, db),
                new EfRepository<TaxGroup>(NullLogger<TaxGroup>.Instance, db),
                new EfRepository<ItemTaxGroup>(NullLogger<ItemTaxGroup>.Instance, db),
                new EfRepository<PaymentTerm>(NullLogger<PaymentTerm>.Instance, db),
                new EfRepository<Bank>(NullLogger<Bank>.Instance, db),
                new EfRepository<Item>(NullLogger<Item>.Instance, db),
                new EfRepository<GeneralLedgerSetting>(NullLogger<GeneralLedgerSetting>.Instance, db),
                new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, db),
                new EfRepository<MainContraAccount>(NullLogger<MainContraAccount>.Instance, db),
                new EfRepository<Core.Domain.Sales.Customer>(NullLogger<Core.Domain.Sales.Customer>.Instance, db),
                new EfRepository<Core.Domain.Purchases.Vendor>(NullLogger<Core.Domain.Purchases.Vendor>.Instance, db),
                auth: Moq.Mock.Of<Core.Authorization.IAccessAuthorizationService>(a =>
                    a.Authorize(Moq.It.IsAny<string>(), Moq.It.IsAny<Core.Authorization.AccessResourceContext>(), Moq.It.IsAny<Core.Authorization.AccessActorContext>())
                    == Core.Authorization.AccessDecision.Allow("test", "test", "test")));

            var service = new SupplierCreditSettlementService(db, tenant, finService, new SettlementCommandCoordinator(db, tenant), new SettlementAccountMappingService(db, tenant));

            var draft = service.SaveDraft(new SaveSupplierCreditDraftRequest(
                null, vendor.Id, null, "VCR-330", new DateTime(2026, 11, 10), new DateTime(2026, 11, 10),
                (int)CreditNoteKind.Standalone, true, "Annual volume rebate", "ACK-330",
                new[] { new SupplierCreditLineInputDto(null, null, null, exp.Id, tax.Id, true, "Rebate", 1m, 300m) }));

            var posted = service.Post(draft.Id, new PostSupplierCreditRequest());
            Assert.Equal(330m, posted.TotalAmount);
            Assert.Equal(330m, posted.AvailableAmount);

            var applied = service.Apply(posted.Id, new ApplySupplierCreditRequest(
                new DateTime(2026, 11, 12),
                new[]
                {
                    new ApplySupplierCreditItemRequest(bill1.Id, 110m),
                    new ApplySupplierCreditItemRequest(bill2.Id, 165m)
                }));

            Assert.Equal(275m, applied.AppliedAmount);
            Assert.Equal(55m, applied.AvailableAmount);

            var aging = new HistoricalAgingService(db);
            var asOfNov = System.Text.Json.JsonSerializer.SerializeToElement(aging.Build(new DateTime(2026, 11, 30), false, true));
            Assert.Equal(35m, asOfNov.GetProperty("payables").GetProperty("postedTotal").GetDecimal());
            Assert.Equal("In balance", asOfNov.GetProperty("payables").GetProperty("reconciliationState").GetString());
        }
    }
}

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
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Inventory;
using Xunit;
using Item = Core.Domain.Items.Item;
using Measurement = Core.Domain.Items.Measurement;
using InventoryControlJournal = Core.Domain.Items.InventoryControlJournal;

namespace AccountGo.BackendTests.Financial
{
    public sealed class ReturnsAndInventoryTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId) => CompanyId = companyId;
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId => "warehouse-1";
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        private static (SqliteConnection Conn, ApiDbContext Db, ReturnsAndInventoryAdjustmentService Service, int CustomerId, int VendorId, int TaxId, int InvAccId, int CogsAccId, int ClearingAccId, int VarianceAccId) Setup()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
            var tenant = new FixedTenantContext(1);
            var db = new ApiDbContext(options, tenant);
            db.Database.EnsureCreated();
            SqliteSchemaUpgrader.Upgrade(conn);

            var company = db.Companies.FirstOrDefault(c => c.Id == 1) ?? new Company { Name = "Co", CompanyCode = "CO", Slug = "co" };
            if (company.Id == 0) { db.Companies.Add(company); db.SaveChanges(); }

            var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id) ?? new CompanySetting { CompanyId = company.Id };
            setting.Currency = "USD";
            setting.CreditsReturnsRefundsEnabled = true;
            setting.BooksStartDate = new DateTime(2026, 1, 1);
            if (setting.Id == 0) db.CompanySettings.Add(setting);

            db.FinancialYears.Add(new FinancialYear { CompanyId = company.Id, FiscalYearCode = "2026", FiscalYearName = "FY 2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
            db.AccountClasses.AddRange(
                new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                new AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                new AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" },
                new AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
            db.Measurements.Add(new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" });
            db.SaveChanges();

            Account AddAcc(string code, string name, AccountClasses cls)
            {
                var acc = new Account { CompanyId = company.Id, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr };
                db.Accounts.Add(acc);
                db.SaveChanges();
                return acc;
            }

            var invAcc = AddAcc("1400", "Inventory", AccountClasses.Assets);
            var cogsAcc = AddAcc("5100", "COGS", AccountClasses.Expense);
            var salesAcc = AddAcc("4000", "Sales", AccountClasses.Revenue);
            var suppCred = AddAcc("1310", "Supplier Credits Asset", AccountClasses.Assets);
            var clearing = AddAcc("1330", "Supplier Return Clearing", AccountClasses.Assets);
            var variance = AddAcc("5150", "Return Valuation Difference", AccountClasses.Expense);
            var inTax = AddAcc("1150", "Input Tax", AccountClasses.Assets);

            db.GeneralLedgerSettings.Add(new GeneralLedgerSetting
            {
                CompanyId = company.Id,
                SupplierCreditAssetAccountId = suppCred.Id,
                SupplierReturnClearingAccountId = clearing.Id,
                ReturnValuationDifferenceAccountId = variance.Id
            });

            var tax = new Tax { CompanyId = company.Id, TaxCode = "VAT10", TaxName = "VAT 10%", Rate = 10m, PurchasingAccountId = inTax.Id, SalesAccountId = inTax.Id, IsActive = true };
            db.Taxes.Add(tax);
            var cp = new Party { CompanyId = company.Id, Name = "Cust", PartyType = PartyTypes.Customer };
            var vp = new Party { CompanyId = company.Id, Name = "Vend", PartyType = PartyTypes.Vendor };
            db.Parties.AddRange(cp, vp);
            db.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = company.Id, PartyId = cp.Id };
            var vendor = new Core.Domain.Purchases.Vendor { CompanyId = company.Id, PartyId = vp.Id };
            db.Customers.Add(customer);
            db.Vendors.Add(vendor);
            db.SaveChanges();

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

            var stockService = new StockService(
                new EfRepository<Item>(NullLogger<Item>.Instance, db),
                new EfRepository<SequenceNumber>(NullLogger<SequenceNumber>.Instance, db),
                new EfRepository<Bank>(NullLogger<Bank>.Instance, db),
                finService);

            var service = new ReturnsAndInventoryAdjustmentService(
                db, tenant, finService, stockService,
                new SettlementCommandCoordinator(db, tenant),
                new SettlementAccountMappingService(db, tenant));

            return (conn, db, service, customer.Id, vendor.Id, tax.Id, invAcc.Id, cogsAcc.Id, clearing.Id, variance.Id);
        }

        [Fact]
        public void C05_CustomerReturn_RestoresOriginalIssueCost120_NotCurrent80()
        {
            var (conn, db, service, customerId, _, _, invAccId, cogsAccId, _, _) = Setup();
            using var _ = conn;
            using var __ = db;
            var measId = db.Measurements.First().Id;

            // Item currently has moving-average cost 80, but original sale was issued at 60/unit
            var item = new Item
            {
                CompanyId = 1, Code = "WIDGET", Description = "Widget",
                InventoryAccountId = invAccId, CostOfGoodsSoldAccountId = cogsAccId,
                QuantityOnHand = 5m, StockValue = 400m, AverageCost = 80m, Cost = 80m
            };
            db.Items.Add(item);
            db.SaveChanges();

            var origIssue = new InventoryControlJournal
            {
                CompanyId = 1, ItemId = item.Id, MeasurementId = measId, DocumentType = DocumentTypes.SalesInvoice,
                Date = new DateTime(2026, 11, 1), OUTQty = 2m, UnitCost = 60m, TotalCost = 120m
            };
            db.InventoryControlJournals.Add(origIssue);
            db.SaveChanges();

            var inv = new SalesInvoiceHeader { CompanyId = 1, CustomerId = customerId, No = "INV-W1", Date = new DateTime(2026, 11, 1), Status = SalesInvoiceStatus.Open };
            inv.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
            {
                CompanyId = 1, ItemId = item.Id, MeasurementId = measId, InventoryControlJournalId = origIssue.Id,
                Quantity = 2m, Amount = 150m
            });
            db.SalesInvoiceHeaders.Add(inv);
            db.SaveChanges();

            var invLine = inv.SalesInvoiceLines.First();
            var ret = service.PostCustomerReturn(new PostCustomerReturnRequest(
                inv.Id, null, new DateTime(2026, 11, 10), "Returned saleable",
                new[] { new CustomerReturnLineInputDto(invLine.Id, 2m, (int)ReturnCondition.Saleable) }));

            Assert.Equal(120m, ret.Lines.Single().TotalRestoredCost);
            var updatedItem = db.Items.First(i => i.Id == item.Id);
            Assert.Equal(7m, updatedItem.QuantityOnHand);
            Assert.Equal(520m, updatedItem.StockValue);
        }

        [Fact]
        public void S04_S05_S06_SupplierReturnClearing_InTransit_And_StockRebate()
        {
            var (conn, db, service, _, vendorId, taxId, invAccId, cogsAccId, clearingAccId, varianceAccId) = Setup();
            using var _ = conn;
            using var __ = db;

            var item = new Item
            {
                CompanyId = 1, Code = "PART", Description = "Part",
                InventoryAccountId = invAccId, CostOfGoodsSoldAccountId = cogsAccId,
                QuantityOnHand = 10m, StockValue = 700m, AverageCost = 70m
            };
            db.Items.Add(item);
            db.SaveChanges();

            // S05: In transit without control transfer -> owned inventory remains unchanged
            var inTransit = service.PostSupplierReturnDispatch(new PostSupplierReturnDispatchRequest(
                vendorId, null, null, new DateTime(2026, 11, 2), null, false, "In transit", null,
                new[] { new SupplierReturnLineInputDto(null, null, item.Id, 2m) }));
            Assert.Equal(0m, inTransit.TotalCarryingCost);
            Assert.Equal(10m, db.Items.First(i => i.Id == item.Id).QuantityOnHand);

            // S04: Control transferred -> 2 units at 70 = 140, credit 160 + 16 tax = 176 -> clearing 0, variance 20
            var dispatch = service.PostSupplierReturnDispatch(new PostSupplierReturnDispatchRequest(
                vendorId, null, null, new DateTime(2026, 11, 5), new DateTime(2026, 11, 5), true, "Defective batch", "RMA-1",
                new[] { new SupplierReturnLineInputDto(null, null, item.Id, 2m) }));
            Assert.Equal(140m, dispatch.TotalCarryingCost);
            Assert.Equal(8m, db.Items.First(i => i.Id == item.Id).QuantityOnHand);

            var matched = service.MatchSupplierReturnCredit(new MatchSupplierReturnCreditRequest(
                dispatch.Id, "VCR-176", new DateTime(2026, 11, 8), 160m, taxId, 16m, "Agreed credit", "ACK-176"));
            Assert.NotNull(matched.SupplierCreditNoteId);

            // Verify clearing nets to 0 and variance is 20 Cr
            var clearingNet = db.GeneralLedgerLines.Where(l => l.AccountId == clearingAccId).Sum(l => l.DrCr == DrOrCrSide.Dr ? l.Amount : -l.Amount);
            Assert.Equal(0m, clearingNet);
            var varCr = db.GeneralLedgerLines.Where(l => l.AccountId == varianceAccId && l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount);
            Assert.Equal(20m, varCr);

            // S06: Value-only stock rebate (60 on-hand + 40 sold COGS)
            var rebate = service.PostStockRebate(new PostStockRebateRequest(
                vendorId, item.Id, new DateTime(2026, 11, 12), "REB-100", 60m, 40m, null, 0m, "Volume rebate", "REB-DOC"));
            Assert.Equal(100m, rebate.TotalAmount);
            var afterRebate = db.Items.First(i => i.Id == item.Id);
            Assert.Equal(8m, afterRebate.QuantityOnHand);
            Assert.Equal(500m, afterRebate.StockValue);
        }
    }
}

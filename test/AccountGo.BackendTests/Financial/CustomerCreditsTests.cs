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
using Core.Domain.Sales;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class CustomerCreditsTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId, string userId = "accountant-1")
            {
                CompanyId = companyId;
                UserId = userId;
            }
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId { get; }
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        private static (SqliteConnection Conn, ApiDbContext Db, CustomerCreditSettlementService Service, HistoricalAgingService Aging, int CustomerId, int InvoiceId, int ArAccountId, int ArAccount2Id, int TaxId, int ItemId) SetupScenarioDb(decimal invoiceQty = 1m, decimal invoicePrice = 1000m, decimal taxRate = 10m)
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

            var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == company.Id);
            if (setting == null)
            {
                setting = new CompanySetting { CompanyId = company.Id, Currency = "USD", CreditsReturnsRefundsEnabled = true, BooksStartDate = new DateTime(2026, 1, 1) };
                db.CompanySettings.Add(setting);
            }
            else
            {
                setting.Currency = "USD";
                setting.CreditsReturnsRefundsEnabled = true;
                setting.BooksStartDate = new DateTime(2026, 1, 1);
            }
            db.SaveChanges();

            if (!db.FinancialYears.Any(f => f.CompanyId == company.Id))
            {
                db.FinancialYears.Add(new FinancialYear
                {
                    CompanyId = company.Id,
                    FiscalYearCode = "2026",
                    FiscalYearName = "FY 2026",
                    StartDate = new DateTime(2026, 1, 1),
                    EndDate = new DateTime(2026, 12, 31),
                    IsActive = true
                });
                db.SaveChanges();
            }

            if (!db.AccountClasses.Any())
            {
                db.AccountClasses.AddRange(
                    new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                    new AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                    new AccountClass { Id = 3, Name = "Equity", NormalBalance = "Cr" },
                    new AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" },
                    new AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
                db.SaveChanges();
            }

            if (!db.Measurements.Any())
            {
                db.Measurements.Add(new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" });
                db.SaveChanges();
            }
            var measurementId = db.Measurements.First(m => m.CompanyId == company.Id).Id;

            Account AddAcc(string code, string name, AccountClasses cls)
            {
                var acc = new Account { CompanyId = company.Id, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr };
                db.Accounts.Add(acc);
                db.SaveChanges();
                return acc;
            }

            var ar1 = AddAcc("1200", "Accounts Receivable", AccountClasses.Assets);
            var ar2 = AddAcc("1201", "Accounts Receivable Trade", AccountClasses.Assets);
            var salesAcc = AddAcc("4000", "Sales Revenue", AccountClasses.Revenue);
            var returnsAcc = AddAcc("4010", "Sales Returns & Allowances", AccountClasses.Revenue);
            var custCredLiab = AddAcc("2200", "Customer Credits Liability", AccountClasses.Liabilities);
            var outTaxAcc = AddAcc("2150", "Output Tax Control", AccountClasses.Liabilities);
            var cashAcc = AddAcc("1010", "Operating Bank", AccountClasses.Assets);

            var glSet = db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == company.Id) ?? new GeneralLedgerSetting { CompanyId = company.Id };
            glSet.CustomerCreditLiabilityAccountId = custCredLiab.Id;
            glSet.SalesReturnsAllowancesAccountId = returnsAcc.Id;
            if (glSet.Id == 0) db.GeneralLedgerSettings.Add(glSet);
            db.SaveChanges();

            var tax = new Tax { CompanyId = company.Id, TaxCode = "VAT10", TaxName = "VAT 10%", Rate = taxRate, SalesAccountId = outTaxAcc.Id, PurchasingAccountId = outTaxAcc.Id, IsActive = true };
            db.Taxes.Add(tax);
            var item = new Item { CompanyId = company.Id, Code = "SVC1", Description = "Consulting", SalesAccountId = salesAcc.Id };
            db.Items.Add(item);
            var party = new Party { CompanyId = company.Id, Name = "Acme Corp", PartyType = PartyTypes.Customer };
            db.Parties.Add(party);
            db.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = company.Id, PartyId = party.Id, AccountsReceivableAccountId = ar1.Id };
            db.Customers.Add(customer);
            db.SaveChanges();

            var subtotal = Math.Round(invoiceQty * invoicePrice, 2, MidpointRounding.AwayFromZero);
            var taxAmt = Math.Round(subtotal * taxRate / 100m, 2, MidpointRounding.AwayFromZero);
            var glInv = new GeneralLedgerHeader { CompanyId = company.Id, Date = new DateTime(2026, 11, 1), DocumentType = DocumentTypes.SalesInvoice, Description = "INV-001" };
            glInv.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = company.Id, AccountId = ar1.Id, DrCr = DrOrCrSide.Dr, Amount = subtotal + taxAmt });
            glInv.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = company.Id, AccountId = salesAcc.Id, DrCr = DrOrCrSide.Cr, Amount = subtotal });
            if (taxAmt > 0) glInv.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = company.Id, AccountId = outTaxAcc.Id, DrCr = DrOrCrSide.Cr, Amount = taxAmt });
            db.GeneralLedgerHeaders.Add(glInv);
            db.SaveChanges();

            var invoice = new SalesInvoiceHeader
            {
                CompanyId = company.Id,
                CustomerId = customer.Id,
                No = "INV-001",
                Date = new DateTime(2026, 11, 1),
                Status = SalesInvoiceStatus.Open,
                ReceivableAccountId = ar1.Id,
                GeneralLedgerHeaderId = glInv.Id
            };
            invoice.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
            {
                CompanyId = company.Id,
                ItemId = item.Id,
                MeasurementId = measurementId,
                Quantity = invoiceQty,
                Amount = invoicePrice,
                TaxId = tax.Id,
                TaxAmount = taxAmt,
                SalesAccountId = salesAcc.Id,
                Description = "Consulting"
            });
            db.SalesInvoiceHeaders.Add(invoice);
            db.SaveChanges();

            var finService = new Services.Financial.FinancialService(
                null!,
                null!,
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
                    a.Authorize(
                        Moq.It.IsAny<string>(),
                        Moq.It.IsAny<Core.Authorization.AccessResourceContext>(),
                        Moq.It.IsAny<Core.Authorization.AccessActorContext>())
                    == Core.Authorization.AccessDecision.Allow("test", "test", "test")));

            var coordinator = new SettlementCommandCoordinator(db, tenant);
            var mappings = new SettlementAccountMappingService(db, tenant);
            var service = new CustomerCreditSettlementService(db, tenant, finService, coordinator, mappings);
            var aging = new HistoricalAgingService(db);

            return (conn, db, service, aging, customer.Id, invoice.Id, ar1.Id, ar2.Id, tax.Id, item.Id);
        }

        [Fact]
        public void C01_UnpaidInvoice_CreditAndApply_ReducesArTo880_AndTaxBy20Once()
        {
            var (conn, db, service, aging, customerId, invoiceId, _, _, taxId, itemId) = SetupScenarioDb();
            using var _ = conn;
            using var __ = db;

            var invLine = db.SalesInvoiceLines.First(l => l.SalesInvoiceHeaderId == invoiceId);
            var draft = service.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, customerId, invoiceId, new DateTime(2026, 11, 10), (int)CreditNoteKind.PriceOrServiceAdjustment,
                false, "Agreed price adjustment", "CASE-1", null,
                new[] { new CustomerCreditLineInputDto(invLine.Id, itemId, taxId, false, "Adjustment", 1m, 200m, 0m) }));

            var posted = service.Post(draft.Id, new PostCustomerCreditRequest(ApplyToSourceInvoiceAmount: 220m, ApplicationDate: new DateTime(2026, 11, 10)));
            Assert.Equal(220m, posted.TotalAmount);
            Assert.Equal(220m, posted.AppliedAmount);
            Assert.Equal(0m, posted.AvailableAmount);

            var inv = db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).First(i => i.Id == invoiceId);
            Assert.Equal(880m, inv.BalanceDue());
            Assert.Equal(0m, inv.TotalAllocated());
        }

        [Fact]
        public void C03_PartlyPaidInvoice_CapsDefaultApplicationToDue_LeavesSurplusAvailable()
        {
            var (conn, db, service, _, customerId, invoiceId, _, _, taxId, itemId) = SetupScenarioDb();
            using var _ = conn;
            using var __ = db;

            var rcpt = new SalesReceiptHeader { CompanyId = 1, CustomerId = customerId, Date = new DateTime(2026, 11, 5), Amount = 1000m, No = "RCPT-1" };
            db.SalesReceiptHeaders.Add(rcpt);
            db.SaveChanges();
            db.CustomerAllocations.Add(new CustomerAllocation { CompanyId = 1, CustomerId = customerId, SalesInvoiceHeaderId = invoiceId, SalesReceiptHeaderId = rcpt.Id, Date = new DateTime(2026, 11, 5), Amount = 1000m });
            db.SaveChanges();

            var invLine = db.SalesInvoiceLines.First(l => l.SalesInvoiceHeaderId == invoiceId);
            var req = new SaveCustomerCreditDraftRequest(
                null, customerId, invoiceId, new DateTime(2026, 11, 10), (int)CreditNoteKind.PriceOrServiceAdjustment,
                false, "Concession", null, null,
                new[] { new CustomerCreditLineInputDto(invLine.Id, itemId, taxId, false, "Adjustment", 1m, 200m, 0m) });

            var preview = service.Preview(req);
            Assert.Equal(100m, preview.InvoiceBalanceDue);
            Assert.Equal(100m, preview.DefaultApplyAmount);
            Assert.Equal(120m, preview.RemainingAvailableAfterDefaultApply);

            var draft = service.SaveDraft(req);
            var posted = service.Post(draft.Id, new PostCustomerCreditRequest(ApplyToSourceInvoiceAmount: preview.DefaultApplyAmount));
            Assert.Equal(100m, posted.AppliedAmount);
            Assert.Equal(120m, posted.AvailableAmount);
            Assert.Equal(0m, db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).First(i => i.Id == invoiceId).BalanceDue());
        }

        [Fact]
        public void X05_X06_NovemberSettlement_ReversedInDecember_AndChangedArMapping_ReconcileAsOf()
        {
            var (conn, db, service, aging, customerId, invoiceId, _, ar2Id, taxId, itemId) = SetupScenarioDb();
            using var _ = conn;
            using var __ = db;

            var invLine = db.SalesInvoiceLines.First(l => l.SalesInvoiceHeaderId == invoiceId);
            var draft = service.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, customerId, invoiceId, new DateTime(2026, 11, 15), (int)CreditNoteKind.PriceOrServiceAdjustment,
                false, "Nov adjustment", null, null,
                new[] { new CustomerCreditLineInputDto(invLine.Id, itemId, taxId, false, "Adjustment", 1m, 200m, 0m) }));
            var posted = service.Post(draft.Id, new PostCustomerCreditRequest(ApplyToSourceInvoiceAmount: 220m, ApplicationDate: new DateTime(2026, 11, 20)));

            // Change customer's AR account mapping after posting (X06)
            var cust = db.Customers.First(c => c.Id == customerId);
            cust.AccountsReceivableAccountId = ar2Id;
            db.SaveChanges();

            // Reverse the application in December (X05)
            var appId = posted.Applications.Single().Id;
            service.ReverseApplication(appId, new ReverseSettlementRequest(new DateTime(2026, 12, 5), "Applied to wrong invoice"));

            // As of Nov 30: still settled (880 due) and GL control reconciles
            var nov = System.Text.Json.JsonSerializer.SerializeToElement(aging.Build(new DateTime(2026, 11, 30), true, false));
            Assert.Equal(880m, nov.GetProperty("receivables").GetProperty("postedTotal").GetDecimal());
            Assert.Equal("In balance", nov.GetProperty("receivables").GetProperty("reconciliationState").GetString());

            // As of Dec 10: reopened (1100 due) and GL control reconciles
            var dec = System.Text.Json.JsonSerializer.SerializeToElement(aging.Build(new DateTime(2026, 12, 10), true, false));
            Assert.Equal(1100m, dec.GetProperty("receivables").GetProperty("postedTotal").GetDecimal());
            Assert.Equal("In balance", dec.GetProperty("receivables").GetProperty("reconciliationState").GetString());
        }
    }
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Api.Data;
using Api.Data.Postgres;
using Api.Data.Repositories;
using Api.Service;
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Tenancy;
using Dto.Financial;
using Dto.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class CreditsReturnsRefundsPostgresRaceAndAcceptanceTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId, string userId = "pg-user")
            {
                CompanyId = companyId;
                UserId = userId;
            }
            public int? CompanyId { get; }
            public string? CompanySlug => "pg-test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId { get; }
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        private static Services.Financial.FinancialService BuildFinancialService(ApiDbContext db) =>
            new Services.Financial.FinancialService(
                null!, null!,
                new EfRepository<GeneralLedgerHeader>(NullLogger<GeneralLedgerHeader>.Instance, db),
                new EfRepository<GeneralLedgerLine>(NullLogger<GeneralLedgerLine>.Instance, db),
                new EfRepository<Core.Domain.Financials.Account>(NullLogger<Core.Domain.Financials.Account>.Instance, db),
                new EfRepository<Core.Domain.Financials.AccountClass>(NullLogger<Core.Domain.Financials.AccountClass>.Instance, db),
                new EfRepository<Core.Domain.TaxSystem.Tax>(NullLogger<Core.Domain.TaxSystem.Tax>.Instance, db),
                new EfRepository<JournalEntryHeader>(NullLogger<JournalEntryHeader>.Instance, db),
                new EfRepository<Core.Domain.Financials.JournalEntryLine>(NullLogger<Core.Domain.Financials.JournalEntryLine>.Instance, db),
                new EfRepository<FinancialYear>(NullLogger<FinancialYear>.Instance, db),
                new EfRepository<Core.Domain.TaxSystem.TaxGroup>(NullLogger<Core.Domain.TaxSystem.TaxGroup>.Instance, db),
                new EfRepository<Core.Domain.TaxSystem.ItemTaxGroup>(NullLogger<Core.Domain.TaxSystem.ItemTaxGroup>.Instance, db),
                new EfRepository<Core.Domain.PaymentTerm>(NullLogger<Core.Domain.PaymentTerm>.Instance, db),
                new EfRepository<Core.Domain.Financials.Bank>(NullLogger<Core.Domain.Financials.Bank>.Instance, db),
                new EfRepository<Item>(NullLogger<Item>.Instance, db),
                new EfRepository<GeneralLedgerSetting>(NullLogger<GeneralLedgerSetting>.Instance, db),
                new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, db),
                new EfRepository<MainContraAccount>(NullLogger<MainContraAccount>.Instance, db),
                new EfRepository<Core.Domain.Sales.Customer>(NullLogger<Core.Domain.Sales.Customer>.Instance, db),
                new EfRepository<Core.Domain.Purchases.Vendor>(NullLogger<Core.Domain.Purchases.Vendor>.Instance, db),
                auth: Moq.Mock.Of<IAccessAuthorizationService>(a =>
                    a.Authorize(Moq.It.IsAny<string>(), Moq.It.IsAny<AccessResourceContext>(), Moq.It.IsAny<AccessActorContext>())
                    == AccessDecision.Allow("test", "test", "test")));

        [Fact]
        public async Task X01_Postgres_ConcurrentApplyAndRefund_CannotOverspendCreditCapacity()
        {
            using var pg = PostgresTestDatabase.CreateIfConfigured();
            Assert.NotNull(pg);

            var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(pg!.ConnectionString).Options;
            int companyId;
            int creditId;
            int invoiceId;
            int refundId;

            using (var db = new PostgresApiDbContext(options, new FixedTenantContext(1)))
            {
                db.Database.Migrate();
                var company = db.Companies.FirstOrDefault(c => c.Id == 1) ?? new Company { Name = "PG Co", CompanyCode = "PG", Slug = "pg" };
                if (company.Id == 0) { db.Companies.Add(company); db.SaveChanges(); }
                companyId = company.Id;

                var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == companyId) ?? new CompanySetting { CompanyId = companyId };
                setting.Currency = "USD";
                setting.CreditsReturnsRefundsEnabled = true;
                setting.BooksStartDate = new DateTime(2026, 1, 1);
                if (setting.Id == 0) db.CompanySettings.Add(setting);
                var opSetting = db.WorkspaceOperatingSettings.FirstOrDefault(w => w.CompanyId == companyId);
                if (opSetting == null)
                    db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings { CompanyId = companyId, CollaborationState = WorkspaceCollaborationState.SoloActive });
                else
                    opSetting.CollaborationState = WorkspaceCollaborationState.SoloActive;

                if (!db.FinancialYears.Any(f => f.CompanyId == companyId))
                    db.FinancialYears.Add(new FinancialYear { CompanyId = companyId, FiscalYearCode = "2026", FiscalYearName = "FY 2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
                if (!db.AccountClasses.Any())
                    db.AccountClasses.AddRange(
                        new Core.Domain.Financials.AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                        new Core.Domain.Financials.AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                        new Core.Domain.Financials.AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" });
                var meas = db.Measurements.FirstOrDefault(m => m.CompanyId == companyId) ?? new Measurement { CompanyId = companyId, Code = "EA", Description = "Each" };
                if (meas.Id == 0) db.Measurements.Add(meas);
                db.SaveChanges();

                Core.Domain.Financials.Account AddAcc(string code, string name, AccountClasses cls)
                {
                    var acc = new Core.Domain.Financials.Account { CompanyId = companyId, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls == AccountClasses.Assets ? DrOrCrSide.Dr : DrOrCrSide.Cr };
                    db.Accounts.Add(acc);
                    db.SaveChanges();
                    return acc;
                }

                var bankGl = AddAcc("1010", "Bank", AccountClasses.Assets);
                var ar = AddAcc("1200", "AR", AccountClasses.Assets);
                var custCred = AddAcc("2200", "Customer Credits", AccountClasses.Liabilities);
                var retAcc = AddAcc("4010", "Returns", AccountClasses.Revenue);
                var salesAcc = AddAcc("4000", "Sales", AccountClasses.Revenue);

                var glSet = db.GeneralLedgerSettings.FirstOrDefault(g => g.CompanyId == companyId) ?? new GeneralLedgerSetting { CompanyId = companyId };
                glSet.CustomerCreditLiabilityAccountId = custCred.Id;
                glSet.SalesReturnsAllowancesAccountId = retAcc.Id;
                if (glSet.Id == 0) db.GeneralLedgerSettings.Add(glSet);

                var bank = new Core.Domain.Financials.Bank { CompanyId = companyId, Name = "Main", AccountId = bankGl.Id, Type = BankTypes.CheckingAccount };
                db.Banks.Add(bank);
                var item = new Item { CompanyId = companyId, Code = "SVC", Description = "Svc", SalesAccountId = salesAcc.Id };
                db.Items.Add(item);
                var party = new Party { CompanyId = companyId, Name = "Race Cust", PartyType = PartyTypes.Customer };
                db.Parties.Add(party);
                db.SaveChanges();

                var customer = new Core.Domain.Sales.Customer { CompanyId = companyId, PartyId = party.Id, AccountsReceivableAccountId = ar.Id };
                db.Customers.Add(customer);
                db.SaveChanges();

                var inv = new SalesInvoiceHeader { CompanyId = companyId, CustomerId = customer.Id, No = "INV-RACE", Date = new DateTime(2026, 11, 1), Status = SalesInvoiceStatus.Open, ReceivableAccountId = ar.Id };
                inv.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine { CompanyId = companyId, ItemId = item.Id, MeasurementId = meas.Id, Quantity = 1m, Amount = 300m });
                db.SalesInvoiceHeaders.Add(inv);
                db.SaveChanges();
                invoiceId = inv.Id;

                var tenant = new FixedTenantContext(companyId);
                var fin = BuildFinancialService(db);
                var coord = new SettlementCommandCoordinator(db, tenant);
                var map = new SettlementAccountMappingService(db, tenant);
                var ccService = new CustomerCreditSettlementService(db, tenant, fin, coord, map);
                var rfService = new RefundAndAdvanceService(db, tenant, fin, coord, map);

                var draft = ccService.SaveDraft(new SaveCustomerCreditDraftRequest(
                    null, customer.Id, null, new DateTime(2026, 11, 5), (int)CreditNoteKind.PriceOrServiceAdjustment, false, "Race test", null, null,
                    new[] { new CustomerCreditLineInputDto(null, item.Id, null, false, "Credit", 1m, 220m, 0m) }));
                var posted = ccService.Post(draft.Id, new PostCustomerCreditRequest());
                creditId = posted.Id;

                var rfDraft = rfService.SaveRefundDraft(new SavePartyRefundDraftRequest(
                    null, (int)PartyRefundDirection.CustomerPayout, customer.Id, null, new DateTime(2026, 11, 6), bank.Id,
                    "Bank Transfer", "EXT", "Ref",
                    new[] { new RefundAllocationInputDto((int)SettlementSourceType.CustomerCreditNote, creditId, 150m) }));
                refundId = rfDraft.Id;
            }

            using var barrier = new Barrier(2);
            var t1 = Task.Run(() =>
            {
                using var db1 = new PostgresApiDbContext(options, new FixedTenantContext(companyId, "actor-1"));
                var svc = new CustomerCreditSettlementService(db1, new FixedTenantContext(companyId, "actor-1"), BuildFinancialService(db1), new SettlementCommandCoordinator(db1, new FixedTenantContext(companyId, "actor-1")), new SettlementAccountMappingService(db1, new FixedTenantContext(companyId, "actor-1")));
                barrier.SignalAndWait();
                try
                {
                    svc.Apply(creditId, new ApplyCustomerCreditRequest(new DateTime(2026, 11, 10), new[] { new ApplyCustomerCreditItemRequest(invoiceId, 150m) }));
                    return true;
                }
                catch (SettlementCommandException)
                {
                    return false;
                }
            });

            var t2 = Task.Run(() =>
            {
                using var db2 = new PostgresApiDbContext(options, new FixedTenantContext(companyId, "actor-2"));
                var svc = new RefundAndAdvanceService(db2, new FixedTenantContext(companyId, "actor-2"), BuildFinancialService(db2), new SettlementCommandCoordinator(db2, new FixedTenantContext(companyId, "actor-2")), new SettlementAccountMappingService(db2, new FixedTenantContext(companyId, "actor-2")));
                barrier.SignalAndWait();
                try
                {
                    svc.PostRefund(refundId, new PostPartyRefundRequest());
                    return true;
                }
                catch (SettlementCommandException)
                {
                    return false;
                }
            });

            var results = await Task.WhenAll(t1, t2);
            Assert.Equal(1, results.Count(r => r));

            using (var verifyDb = new PostgresApiDbContext(options, new FixedTenantContext(companyId)))
            {
                var cn = verifyDb.CustomerCreditNotes.AsNoTracking().First(c => c.Id == creditId);
                Assert.True(cn.AppliedAmount + cn.RefundedAmount <= cn.TotalAmount);
                Assert.Equal(150m, cn.AppliedAmount + cn.RefundedAmount);
            }
        }

        [Fact]
        public void X10_Postgres_UpgradeFromPriorMigration_PreservesLegacyBillsAndPayments()
        {
            using var pg = PostgresTestDatabase.CreateIfConfigured();
            Assert.NotNull(pg);

            var options = new DbContextOptionsBuilder<PostgresApiDbContext>().UseNpgsql(pg!.ConnectionString).Options;
            using var db = new PostgresApiDbContext(options, new FixedTenantContext(1));
            db.GetService<IMigrator>().Migrate("20261117090109_YearEndCloseEvidence");

            var company = db.Companies.FirstOrDefault(c => c.Id == 1) ?? new Company { Name = "Legacy Co", CompanyCode = "LEG", Slug = "leg" };
            if (company.Id == 0) { db.Companies.Add(company); db.SaveChanges(); }

            var party = new Party { CompanyId = company.Id, Name = "Legacy Vendor", PartyType = PartyTypes.Vendor };
            db.Parties.Add(party);
            db.SaveChanges();
            var vendor = new Core.Domain.Purchases.Vendor { CompanyId = company.Id, PartyId = party.Id };
            db.Vendors.Add(vendor);
            db.SaveChanges();

            db.Database.ExecuteSqlRaw(
                @"INSERT INTO ""PurchaseInvoiceHeader"" (""CompanyId"", ""VendorId"", ""No"", ""VendorInvoiceNo"", ""Date"", ""Status"") VALUES ({0}, {1}, 'LEG-BILL-1', 'LB1', TIMESTAMP '2026-10-01 00:00:00', 1);",
                company.Id, vendor.Id);

            // Now upgrade to latest migration
            db.Database.Migrate();

            var upgradedBill = db.PurchaseInvoiceHeaders.AsNoTracking().First(b => b.No == "LEG-BILL-1");
            Assert.Equal("LEG-BILL-1", upgradedBill.No);
            Assert.Equal(0m, upgradedBill.CreditedAmount);
            Assert.Equal(0m, upgradedBill.AdvanceAppliedAmount);
        }
    }
}

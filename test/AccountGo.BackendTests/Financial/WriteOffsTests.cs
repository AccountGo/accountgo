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
using Core.Tenancy;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class WriteOffsTests
    {
        private sealed class MutableTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public MutableTenantContext(int companyId, string userId = "collector-1")
            {
                CompanyId = companyId;
                UserId = userId;
            }
            public int? CompanyId { get; }
            public string? CompanySlug => "test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId { get; set; }
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        [Fact]
        public void C07_C08_WriteOff400WithAllowance250_Recover50_AndBlockDoubleReliefCredit()
        {
            using var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
            var tenant = new MutableTenantContext(1, "collector-1");
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
            var opSetting = db.WorkspaceOperatingSettings.FirstOrDefault(w => w.CompanyId == company.Id);
            if (opSetting == null)
                db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings { CompanyId = company.Id, CollaborationState = WorkspaceCollaborationState.SoloActive });
            else
                opSetting.CollaborationState = WorkspaceCollaborationState.SoloActive;

            db.FinancialYears.Add(new FinancialYear { CompanyId = company.Id, FiscalYearCode = "2026", FiscalYearName = "FY 2026", StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 12, 31), IsActive = true });
            db.AccountClasses.AddRange(
                new Core.Domain.Financials.AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                new Core.Domain.Financials.AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
            var meas = new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" };
            db.Measurements.Add(meas);
            db.SaveChanges();

            Core.Domain.Financials.Account AddAcc(string code, string name, AccountClasses cls)
            {
                var acc = new Core.Domain.Financials.Account { CompanyId = company.Id, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr };
                db.Accounts.Add(acc);
                db.SaveChanges();
                return acc;
            }

            var bankGl = AddAcc("1010", "Operating Bank", AccountClasses.Assets);
            var ar = AddAcc("1200", "Accounts Receivable", AccountClasses.Assets);
            var allowance = AddAcc("1210", "Allowance for Doubtful Accounts", AccountClasses.Assets);
            var impairment = AddAcc("6200", "Bad Debt Impairment Expense", AccountClasses.Expense);
            var recoveryAcc = AddAcc("4200", "Bad Debt Recovery", AccountClasses.Revenue);
            var custCred = AddAcc("2200", "Customer Credits", AccountClasses.Liabilities);
            var returnsAcc = AddAcc("4010", "Sales Returns", AccountClasses.Revenue);
            var salesAcc = AddAcc("4000", "Sales Revenue", AccountClasses.Revenue);

            db.GeneralLedgerSettings.Add(new GeneralLedgerSetting
            {
                CompanyId = company.Id,
                ReceivableLossAllowanceAccountId = allowance.Id,
                ImpairmentExpenseAccountId = impairment.Id,
                BadDebtRecoveryAccountId = recoveryAcc.Id,
                CustomerCreditLiabilityAccountId = custCred.Id,
                SalesReturnsAllowancesAccountId = returnsAcc.Id
            });

            var bank = new Core.Domain.Financials.Bank { CompanyId = company.Id, Name = "Main Bank", AccountId = bankGl.Id, Type = BankTypes.CheckingAccount };
            db.Banks.Add(bank);
            var item = new Item { CompanyId = company.Id, Code = "SVC", Description = "Service", SalesAccountId = salesAcc.Id };
            db.Items.Add(item);
            var party = new Party { CompanyId = company.Id, Name = "Bankrupt Customer", PartyType = PartyTypes.Customer };
            db.Parties.Add(party);
            db.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = company.Id, PartyId = party.Id, AccountsReceivableAccountId = ar.Id };
            db.Customers.Add(customer);
            db.SaveChanges();

            var invoice = new SalesInvoiceHeader
            {
                CompanyId = company.Id,
                CustomerId = customer.Id,
                No = "INV-400",
                Date = new DateTime(2026, 10, 1),
                Status = SalesInvoiceStatus.Open,
                ReceivableAccountId = ar.Id
            };
            invoice.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine
            {
                CompanyId = company.Id,
                ItemId = item.Id,
                MeasurementId = meas.Id,
                Quantity = 1m,
                Amount = 400m
            });
            db.SalesInvoiceHeaders.Add(invoice);
            db.SaveChanges();

            var finService = new Services.Financial.FinancialService(
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
                auth: Moq.Mock.Of<Core.Authorization.IAccessAuthorizationService>(a =>
                    a.Authorize(Moq.It.IsAny<string>(), Moq.It.IsAny<Core.Authorization.AccessResourceContext>(), Moq.It.IsAny<Core.Authorization.AccessActorContext>())
                    == Core.Authorization.AccessDecision.Allow("test", "test", "test")));

            var coord = new SettlementCommandCoordinator(db, tenant);
            var map = new SettlementAccountMappingService(db, tenant);
            var woService = new WriteOffService(db, tenant, finService, coord, map);
            var creditService = new CustomerCreditSettlementService(db, tenant, finService, coord, map);

            // C07: Request write-off 400 with allowance 250
            var req = woService.RequestWriteOff(new RequestWriteOffCommand(
                invoice.Id, new DateTime(2026, 11, 15), 400m, 250m, "Insolvency confirmed", "Court liquidation notice #123"));
            Assert.Equal(150m, req.ImpairmentShortfallAmount);

            tenant.UserId = "reviewer-2";
            var posted = woService.Post(req.Id, new PostWriteOffCommand());

            var invAfter = db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).First(i => i.Id == invoice.Id);
            Assert.Equal(0m, invAfter.BalanceDue());
            Assert.Equal(400m, invAfter.WrittenOffAmount);
            Assert.Equal(0m, invAfter.TotalAllocated());

            // Verify GL: 150 impairment Dr/Cr allowance, plus 400 allowance Dr / Cr AR
            var glLines = db.GeneralLedgerLines.Where(l => l.GeneralLedgerHeaderId == posted.GeneralLedgerHeaderId).ToList();
            Assert.Equal(150m, glLines.Where(l => l.AccountId == impairment.Id && l.DrCr == DrOrCrSide.Dr).Sum(l => l.Amount));
            Assert.Equal(400m, glLines.Where(l => l.AccountId == ar.Id && l.DrCr == DrOrCrSide.Cr).Sum(l => l.Amount));

            // C08: Attempt credit on already written-off invoice must be blocked
            var invLine = invAfter.SalesInvoiceLines.First();
            var blocked = Assert.Throws<SettlementCommandException>(() => creditService.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, customer.Id, invoice.Id, new DateTime(2026, 11, 20), (int)CreditNoteKind.PriceOrServiceAdjustment,
                false, "Duplicate relief attempt", null, null,
                new[] { new CustomerCreditLineInputDto(invLine.Id, item.Id, null, false, "Adj", 1m, 100m, 0m) })));
            Assert.Contains("written off", blocked.Message, StringComparison.OrdinalIgnoreCase);

            // Recover 50 later
            var recovered = woService.RecordRecovery(posted.Id, new RecordWriteOffRecoveryCommand(
                new DateTime(2026, 12, 1), 50m, bank.Id, "DEP-50", "Liquidator dividend"));
            Assert.Equal(50m, recovered.RecoveredAmount);
        }
    }
}

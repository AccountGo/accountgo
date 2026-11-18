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
using Dto.Financial;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class RefundsAndAdvancesTests
    {
        private sealed class MutableTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public MutableTenantContext(int companyId, string userId = "user-1")
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

        private static (SqliteConnection Conn, ApiDbContext Db, MutableTenantContext Tenant, CustomerCreditSettlementService CustCredits, SupplierCreditSettlementService SuppCredits, RefundAndAdvanceService Refunds, int CustomerId, int VendorId, int BankId, int TaxId, int ExpAccId, int SalesAccId, int ArAccId, int ApAccId) Setup()
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(conn).Options;
            var tenant = new MutableTenantContext(1, "preparer-1");
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
                new Core.Domain.Financials.AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                new Core.Domain.Financials.AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
            db.Measurements.Add(new Measurement { CompanyId = company.Id, Code = "EA", Description = "Each" });
            db.SaveChanges();

            Core.Domain.Financials.Account AddAcc(string code, string name, AccountClasses cls, bool isCash = false)
            {
                var acc = new Core.Domain.Financials.Account { CompanyId = company.Id, AccountCode = code, AccountName = name, AccountClassId = (int)cls, DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr, IsCash = isCash };
                db.Accounts.Add(acc);
                db.SaveChanges();
                return acc;
            }

            var bankGl = AddAcc("1010", "Operating Bank", AccountClasses.Assets, true);
            var ar = AddAcc("1200", "Accounts Receivable", AccountClasses.Assets);
            var ap = AddAcc("2000", "Accounts Payable", AccountClasses.Liabilities);
            var custCred = AddAcc("2200", "Customer Credits Liability", AccountClasses.Liabilities);
            var suppCred = AddAcc("1310", "Supplier Credits Asset", AccountClasses.Assets);
            var suppAdv = AddAcc("1320", "Supplier Advances", AccountClasses.Assets);
            var salesAcc = AddAcc("4000", "Sales", AccountClasses.Revenue);
            var retAcc = AddAcc("4010", "Sales Returns", AccountClasses.Revenue);
            var expAcc = AddAcc("5000", "Purchases Expense", AccountClasses.Expense);
            var taxAcc = AddAcc("2150", "Tax Control", AccountClasses.Liabilities);

            db.GeneralLedgerSettings.Add(new GeneralLedgerSetting
            {
                CompanyId = company.Id,
                PayableAccountId = ap.Id,
                CustomerCreditLiabilityAccountId = custCred.Id,
                SalesReturnsAllowancesAccountId = retAcc.Id,
                SupplierCreditAssetAccountId = suppCred.Id,
                SupplierAdvanceAccountId = suppAdv.Id
            });

            var bank = new Core.Domain.Financials.Bank { CompanyId = company.Id, Name = "Main Bank", AccountId = bankGl.Id, Type = BankTypes.CheckingAccount };
            db.Banks.Add(bank);
            var tax = new Tax { CompanyId = company.Id, TaxCode = "VAT10", TaxName = "VAT 10%", Rate = 10m, SalesAccountId = taxAcc.Id, PurchasingAccountId = taxAcc.Id, IsActive = true };
            db.Taxes.Add(tax);
            var custParty = new Party { CompanyId = company.Id, Name = "Customer 1", PartyType = PartyTypes.Customer };
            var vendParty = new Party { CompanyId = company.Id, Name = "Vendor 1", PartyType = PartyTypes.Vendor };
            db.Parties.AddRange(custParty, vendParty);
            db.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = company.Id, PartyId = custParty.Id, AccountsReceivableAccountId = ar.Id };
            var vendor = new Core.Domain.Purchases.Vendor { CompanyId = company.Id, PartyId = vendParty.Id, AccountsPayableAccountId = ap.Id };
            db.Customers.Add(customer);
            db.Vendors.Add(vendor);
            db.SaveChanges();

            var finService = new Services.Financial.FinancialService(
                null!, null!,
                new EfRepository<GeneralLedgerHeader>(NullLogger<GeneralLedgerHeader>.Instance, db),
                new EfRepository<GeneralLedgerLine>(NullLogger<GeneralLedgerLine>.Instance, db),
                new EfRepository<Core.Domain.Financials.Account>(NullLogger<Core.Domain.Financials.Account>.Instance, db),
                new EfRepository<Core.Domain.Financials.AccountClass>(NullLogger<Core.Domain.Financials.AccountClass>.Instance, db),
                new EfRepository<Tax>(NullLogger<Tax>.Instance, db),
                new EfRepository<JournalEntryHeader>(NullLogger<JournalEntryHeader>.Instance, db),
                new EfRepository<Core.Domain.Financials.JournalEntryLine>(NullLogger<Core.Domain.Financials.JournalEntryLine>.Instance, db),
                new EfRepository<FinancialYear>(NullLogger<FinancialYear>.Instance, db),
                new EfRepository<TaxGroup>(NullLogger<TaxGroup>.Instance, db),
                new EfRepository<ItemTaxGroup>(NullLogger<ItemTaxGroup>.Instance, db),
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
            return (
                conn, db, tenant,
                new CustomerCreditSettlementService(db, tenant, finService, coord, map),
                new SupplierCreditSettlementService(db, tenant, finService, coord, map),
                new RefundAndAdvanceService(db, tenant, finService, coord, map),
                customer.Id, vendor.Id, bank.Id, tax.Id, expAcc.Id, salesAcc.Id, ar.Id, ap.Id);
        }

        [Fact]
        public void C02_PaidInvoice_Credit220_Refund150_Apply70ToLaterInvoice()
        {
            var (conn, db, _, custCredits, _, refunds, customerId, _, bankId, taxId, _, salesAccId, arAccId, _) = Setup();
            using var _ = conn;
            using var __ = db;
            var opSetting = db.WorkspaceOperatingSettings.FirstOrDefault(w => w.CompanyId == 1);
            if (opSetting == null)
                db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings { CompanyId = 1, CollaborationState = WorkspaceCollaborationState.SoloActive });
            else
                opSetting.CollaborationState = WorkspaceCollaborationState.SoloActive;
            db.SaveChanges();

            var item = new Item { CompanyId = 1, Code = "SVC", Description = "Service", SalesAccountId = salesAccId };
            db.Items.Add(item);
            db.SaveChanges();
            var measId = db.Measurements.First().Id;

            SalesInvoiceHeader MakeInvoice(string no, decimal net, decimal taxAmt, bool paid)
            {
                var inv = new SalesInvoiceHeader { CompanyId = 1, CustomerId = customerId, No = no, Date = new DateTime(2026, 11, 1), Status = paid ? SalesInvoiceStatus.Closed : SalesInvoiceStatus.Open, ReceivableAccountId = arAccId };
                inv.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine { CompanyId = 1, ItemId = item.Id, MeasurementId = measId, Quantity = 1, Amount = net, TaxId = taxId, TaxAmount = taxAmt });
                db.SalesInvoiceHeaders.Add(inv);
                db.SaveChanges();
                if (paid)
                {
                    var rcpt = new SalesReceiptHeader { CompanyId = 1, CustomerId = customerId, Date = new DateTime(2026, 11, 2), Amount = net + taxAmt, No = "R-" + no };
                    db.SalesReceiptHeaders.Add(rcpt);
                    db.SaveChanges();
                    db.CustomerAllocations.Add(new CustomerAllocation { CompanyId = 1, CustomerId = customerId, SalesInvoiceHeaderId = inv.Id, SalesReceiptHeaderId = rcpt.Id, Date = new DateTime(2026, 11, 2), Amount = net + taxAmt });
                    db.SaveChanges();
                }
                return inv;
            }

            var inv1 = MakeInvoice("INV-1", 1000m, 100m, paid: true);
            var inv2 = MakeInvoice("INV-2", 200m, 0m, paid: false);

            var inv1Line = db.SalesInvoiceLines.First(l => l.SalesInvoiceHeaderId == inv1.Id);
            var cn = custCredits.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, customerId, inv1.Id, new DateTime(2026, 11, 5), (int)CreditNoteKind.PriceOrServiceAdjustment, false, "Concession", null, null,
                new[] { new CustomerCreditLineInputDto(inv1Line.Id, item.Id, taxId, false, "Adj", 1m, 200m, 0m) }));
            var postedCn = custCredits.Post(cn.Id, new PostCustomerCreditRequest());
            Assert.Equal(220m, postedCn.AvailableAmount);

            var rf = refunds.SaveRefundDraft(new SavePartyRefundDraftRequest(
                null, (int)PartyRefundDirection.CustomerPayout, customerId, null, new DateTime(2026, 11, 6), bankId,
                "Bank Transfer", "EXT-150", "Bank confirmation",
                new[] { new RefundAllocationInputDto((int)SettlementSourceType.CustomerCreditNote, postedCn.Id, 150m) }));
            var postedRf = refunds.PostRefund(rf.Id, new PostPartyRefundRequest());
            Assert.Equal(150m, postedRf.Amount);

            var afterApply = custCredits.Apply(postedCn.Id, new ApplyCustomerCreditRequest(
                new DateTime(2026, 11, 7),
                new[] { new ApplyCustomerCreditItemRequest(inv2.Id, 70m) }));

            Assert.Equal(0m, afterApply.AvailableAmount);
            Assert.Equal(150m, afterApply.RefundedAmount);
            Assert.Equal(70m, afterApply.AppliedAmount);
            Assert.Equal(130m, db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).First(i => i.Id == inv2.Id).BalanceDue());
        }

        [Fact]
        public void S02_S03_X11_SupplierAdvance_And_MixedSourceSupplierRefund_Work()
        {
            var (conn, db, tenant, _, suppCredits, refunds, _, vendorId, bankId, taxId, expAccId, _, _, apAccId) = Setup();
            using var _ = conn;
            using var __ = db;

            var item = new Item { CompanyId = 1, Code = "EXP", Description = "Service" };
            db.Items.Add(item);
            db.SaveChanges();

            // S02: Advance 300 before bill 550, apply 300
            var advDraft = refunds.SaveAdvanceDraft(new SaveSupplierAdvanceDraftRequest(
                null, vendorId, null, new DateTime(2026, 11, 1), 300m, bankId, "Deposit for order", true, "WIRE-300", null));
            refunds.SubmitAdvance(advDraft.Id);
            tenant.UserId = "reviewer-2";
            var advPosted = refunds.PostAdvance(advDraft.Id, new PostPartyRefundRequest());
            tenant.UserId = "user-1";

            var bill = new PurchaseInvoiceHeader { CompanyId = 1, VendorId = vendorId, No = "BILL-550", VendorInvoiceNo = "V-550", Date = new DateTime(2026, 11, 5), Status = PurchaseInvoiceStatus.Open, PayableAccountId = apAccId };
            bill.PurchaseInvoiceLines.Add(new PurchaseInvoiceLine { CompanyId = 1, ItemId = item.Id, AccountId = expAccId, Quantity = 1m, Amount = 550m });
            db.PurchaseInvoiceHeaders.Add(bill);
            db.SaveChanges();

            var advApplied = refunds.ApplyAdvance(advPosted.Id, new ApplySupplierAdvanceRequest(
                new DateTime(2026, 11, 6), new[] { new ApplySupplierAdvanceItemRequest(bill.Id, 200m) }));
            Assert.Equal(100m, advApplied.AvailableAmount);
            Assert.Equal(350m, db.PurchaseInvoiceHeaders.Include(b => b.PurchaseInvoiceLines).Include(b => b.VendorPayments).First(b => b.Id == bill.Id).BalanceDue());

            // X11: Mixed-source supplier refund (100 from advance + 55 from standalone supplier credit = single 155 bank inflow)
            var scDraft = suppCredits.SaveDraft(new SaveSupplierCreditDraftRequest(
                null, vendorId, null, "VCR-55", new DateTime(2026, 11, 4), new DateTime(2026, 11, 4),
                (int)CreditNoteKind.Standalone, true, "Overcharge", "ACK-55",
                new[] { new SupplierCreditLineInputDto(null, null, null, expAccId, null, false, "Overcharge", 1m, 55m) }));
            suppCredits.SubmitForReview(scDraft.Id);
            tenant.UserId = "reviewer-2";
            var scPosted = suppCredits.Post(scDraft.Id, new PostSupplierCreditRequest());
            tenant.UserId = "user-1";

            // Test team non-self-approval (X08): submitter cannot post their own refund
            var mixedRefundDraft = refunds.SaveRefundDraft(new SavePartyRefundDraftRequest(
                null, (int)PartyRefundDirection.SupplierInflow, null, vendorId, new DateTime(2026, 11, 10), bankId,
                "Bank Transfer", "INFLOW-155", "Bank credit advice",
                new[]
                {
                    new RefundAllocationInputDto((int)SettlementSourceType.SupplierAdvance, advPosted.Id, 100m),
                    new RefundAllocationInputDto((int)SettlementSourceType.SupplierCreditNote, scPosted.Id, 55m)
                }));
            refunds.SubmitRefund(mixedRefundDraft.Id);

            var selfApprovalEx = Assert.Throws<SettlementCommandException>(() => refunds.PostRefund(mixedRefundDraft.Id, new PostPartyRefundRequest()));
            Assert.Equal(SettlementErrorCodes.SelfApprovalForbidden, selfApprovalEx.Code);

            tenant.UserId = "reviewer-2";
            var mixedPosted = refunds.PostRefund(mixedRefundDraft.Id, new PostPartyRefundRequest());
            Assert.Equal(155m, mixedPosted.Amount);
            Assert.Equal(2, mixedPosted.Allocations.Count);
        }
    }
}

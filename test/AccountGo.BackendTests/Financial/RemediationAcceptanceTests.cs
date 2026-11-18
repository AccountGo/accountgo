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
using Core.Authorization;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Domain.TaxSystem;
using Core.Tenancy;
using Dto.Financial;
using Dto.Inventory;
using Dto.Purchasing;
using Dto.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public sealed class RemediationAcceptanceTests
    {
        private sealed class FixedTenantContext : ITenantContext
        {
            private sealed class NoopScope : IDisposable { public void Dispose() { } }
            public FixedTenantContext(int companyId, string userId = "user-1")
            {
                CompanyId = companyId;
                UserId = userId;
            }
            public int? CompanyId { get; }
            public string? CompanySlug => "remediation-test";
            public CompanyMemberRole? Role => CompanyMemberRole.Owner;
            public string? UserId { get; set; }
            public bool IsResolved => CompanyId.HasValue;
            public IReadOnlySet<string> Permissions => new HashSet<string>();
            public int RequireCompanyId() => CompanyId ?? throw new InvalidOperationException("No tenant");
            public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) => new NoopScope();
        }

        private static EfRepository<T> Repo<T>(ApiDbContext db) where T : BaseEntity =>
            new(NullLogger<T>.Instance, db);

        private static (SqliteConnection Conn, ApiDbContext Db, FixedTenantContext Tenant,
            CustomerCreditSettlementService CustCredits, SupplierCreditSettlementService SuppCredits,
            RefundAndAdvanceService Refunds, WriteOffService WriteOffs,
            ReturnsAndInventoryAdjustmentService Returns, YearEndCloseService YearEnd,
            StockService StockSvc,
            int CustomerId, int VendorId, int BankId, int ItemId, int MeasId,
            int ArAccId, int ArAltAccId, int ApAccId, int SalesAccId, int InvAccId, int CogsAccId, int ExpAccId)
            CreateFixture(bool isSolo = true, IAccessAuthorizationService? auth = null)
        {
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();

            var tenant = new FixedTenantContext(1, "user-1");
            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseSqlite(conn)
                .Options;
            var db = new ApiDbContext(options, tenant);
            db.Database.EnsureCreated();
            SqliteSchemaUpgrader.Upgrade(conn);

            var company = db.Companies.FirstOrDefault() ?? new Company { Id = 1, CompanyCode = "TST", Name = "Test Co" };
            if (company.Id == 0 || !db.Companies.Any())
            {
                company.Id = 1;
                db.Companies.Add(company);
            }
            var setting = db.CompanySettings.FirstOrDefault(s => s.CompanyId == 1) ?? new CompanySetting { CompanyId = 1 };
            setting.Currency = "USD";
            setting.CreditsReturnsRefundsEnabled = true;
            setting.BooksStartDate = new DateTime(2026, 1, 1);
            if (setting.Id == 0) db.CompanySettings.Add(setting);

            var opSetting = db.WorkspaceOperatingSettings.FirstOrDefault(w => w.CompanyId == 1);
            if (opSetting == null)
            {
                db.WorkspaceOperatingSettings.Add(new WorkspaceOperatingSettings
                {
                    CompanyId = 1,
                    CollaborationState = isSolo ? WorkspaceCollaborationState.SoloActive : WorkspaceCollaborationState.TeamActive
                });
            }
            else
            {
                opSetting.CollaborationState = isSolo ? WorkspaceCollaborationState.SoloActive : WorkspaceCollaborationState.TeamActive;
            }

            db.FinancialYears.Add(new FinancialYear
            {
                CompanyId = 1,
                FiscalYearCode = "2026",
                FiscalYearName = "FY 2026",
                StartDate = new DateTime(2026, 1, 1),
                EndDate = new DateTime(2026, 12, 31),
                IsActive = true
            });
            db.AccountClasses.AddRange(
                new Core.Domain.Financials.AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" },
                new Core.Domain.Financials.AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 3, Name = "Equity", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 4, Name = "Revenue", NormalBalance = "Cr" },
                new Core.Domain.Financials.AccountClass { Id = 5, Name = "Expense", NormalBalance = "Dr" });
            var meas = new Core.Domain.Items.Measurement { CompanyId = 1, Code = "EA", Description = "Each" };
            db.Measurements.Add(meas);
            db.SaveChanges();

            Core.Domain.Financials.Account AddAcc(string code, string name, AccountClasses cls, bool isContra = false)
            {
                var a = new Core.Domain.Financials.Account
                {
                    CompanyId = 1,
                    AccountCode = code,
                    AccountName = name,
                    AccountClassId = (int)cls,
                    DrOrCrSide = cls is AccountClasses.Assets or AccountClasses.Expense ? DrOrCrSide.Dr : DrOrCrSide.Cr,
                    IsContraAccount = isContra
                };
                db.Accounts.Add(a);
                return a;
            }

            var bankGl = AddAcc("10100", "Cash", AccountClasses.Assets);
            var arAcc = AddAcc("10120", "Accounts Receivable", AccountClasses.Assets);
            var arAltAcc = AddAcc("10121", "Accounts Receivable Alt", AccountClasses.Assets);
            var apAcc = AddAcc("20110", "Accounts Payable", AccountClasses.Liabilities);
            var invAcc = AddAcc("10200", "Inventory", AccountClasses.Assets);
            var salesAcc = AddAcc("40100", "Sales", AccountClasses.Revenue);
            var retAcc = AddAcc("40200", "Sales Returns", AccountClasses.Revenue, isContra: true);
            var cogsAcc = AddAcc("50100", "COGS", AccountClasses.Expense);
            var expAcc = AddAcc("50200", "Operating Expense", AccountClasses.Expense);
            var custCredLiab = AddAcc("20410", "Customer Credits Liability", AccountClasses.Liabilities);
            var suppCredAsset = AddAcc("10310", "Supplier Credits Receivable", AccountClasses.Assets);
            var suppAdvAsset = AddAcc("10320", "Supplier Advances", AccountClasses.Assets);
            var suppRetClear = AddAcc("10330", "Supplier Return Clearing", AccountClasses.Assets);
            var retVar = AddAcc("50310", "Return Valuation Difference", AccountClasses.Expense);
            var allowanceAcc = AddAcc("10125", "Allowance for Doubtful Accounts", AccountClasses.Assets, isContra: true);
            var badDebtAcc = AddAcc("50410", "Bad Debt Expense", AccountClasses.Expense);
            var recoveryAcc = AddAcc("40410", "Bad Debt Recovery", AccountClasses.Revenue);
            var permEq = AddAcc("30200", "Retained Earnings", AccountClasses.Equity);
            db.SaveChanges();

            db.GeneralLedgerSettings.Add(new GeneralLedgerSetting
            {
                CompanyId = 1,
                PayableAccountId = apAcc.Id,
                PermanentAccountId = permEq.Id,
                CustomerCreditLiabilityAccountId = custCredLiab.Id,
                SalesReturnsAllowancesAccountId = retAcc.Id,
                SupplierCreditAssetAccountId = suppCredAsset.Id,
                SupplierAdvanceAccountId = suppAdvAsset.Id,
                SupplierReturnClearingAccountId = suppRetClear.Id,
                ReturnValuationDifferenceAccountId = retVar.Id,
                ReceivableLossAllowanceAccountId = allowanceAcc.Id,
                ImpairmentExpenseAccountId = badDebtAcc.Id,
                BadDebtRecoveryAccountId = recoveryAcc.Id
            });

            var bank = new Core.Domain.Financials.Bank
            {
                CompanyId = 1, Name = "Main Checking", AccountId = bankGl.Id, Type = BankTypes.CheckingAccount, IsDefault = true, IsActive = true
            };
            db.Banks.Add(bank);

            var custParty = new Party { CompanyId = 1, Name = "Acme Customer", PartyType = PartyTypes.Customer };
            var vendParty = new Party { CompanyId = 1, Name = "Beta Vendor", PartyType = PartyTypes.Vendor };
            db.Parties.AddRange(custParty, vendParty);
            db.SaveChanges();

            var customer = new Core.Domain.Sales.Customer { CompanyId = 1, PartyId = custParty.Id, AccountsReceivableAccountId = arAcc.Id, SalesAccountId = salesAcc.Id };
            var vendor = new Core.Domain.Purchases.Vendor { CompanyId = 1, PartyId = vendParty.Id, AccountsPayableAccountId = apAcc.Id };
            db.Customers.Add(customer);
            db.Vendors.Add(vendor);

            var item = new Core.Domain.Items.Item
            {
                CompanyId = 1,
                Code = "WIDGET",
                Description = "Widget",
                PurchaseMeasurementId = meas.Id,
                SellMeasurementId = meas.Id,
                SmallestMeasurementId = meas.Id,
                InventoryAccountId = invAcc.Id,
                CostOfGoodsSoldAccountId = cogsAcc.Id,
                SalesAccountId = salesAcc.Id,
                AverageCost = 10m,
                QuantityOnHand = 0m,
                StockValue = 0m
            };
            db.Items.Add(item);
            db.SaveChanges();

            var lockGuard = new BooksLockGuard(
                Repo<CompanySetting>(db),
                Moq.Mock.Of<Core.Entitlements.IEntitlementService>(),
                tenant);
            var allowAuth = Moq.Mock.Of<IAccessAuthorizationService>(a =>
                a.Authorize(Moq.It.IsAny<string>(), Moq.It.IsAny<AccessResourceContext>(), Moq.It.IsAny<AccessActorContext>())
                == AccessDecision.Allow("test", "test", "test"));
            var finSvc = new Services.Financial.FinancialService(
                null!,
                null!,
                Repo<GeneralLedgerHeader>(db),
                Repo<GeneralLedgerLine>(db),
                Repo<Core.Domain.Financials.Account>(db),
                Repo<Core.Domain.Financials.AccountClass>(db),
                Repo<Tax>(db),
                Repo<JournalEntryHeader>(db),
                Repo<Core.Domain.Financials.JournalEntryLine>(db),
                Repo<FinancialYear>(db),
                Repo<TaxGroup>(db),
                Repo<ItemTaxGroup>(db),
                Repo<Core.Domain.PaymentTerm>(db),
                Repo<Core.Domain.Financials.Bank>(db),
                Repo<Core.Domain.Items.Item>(db),
                Repo<GeneralLedgerSetting>(db),
                Repo<CompanySetting>(db),
                Repo<MainContraAccount>(db),
                Repo<Core.Domain.Sales.Customer>(db),
                Repo<Core.Domain.Purchases.Vendor>(db),
                booksLockGuard: lockGuard,
                auth: allowAuth);
            var stockSvc = new StockService(
                Repo<Core.Domain.Items.Item>(db),
                Repo<SequenceNumber>(db),
                Repo<Core.Domain.Financials.Bank>(db),
                finSvc);

            var coord = new SettlementCommandCoordinator(db, tenant, auth);
            var mapSvc = new SettlementAccountMappingService(db, tenant);

            var custCredits = new CustomerCreditSettlementService(db, tenant, finSvc, coord, mapSvc);
            var suppCredits = new SupplierCreditSettlementService(db, tenant, finSvc, coord, mapSvc);
            var refunds = new RefundAndAdvanceService(db, tenant, finSvc, coord, mapSvc);
            var writeOffs = new WriteOffService(db, tenant, finSvc, coord, mapSvc);
            var returns = new ReturnsAndInventoryAdjustmentService(db, tenant, finSvc, stockSvc, coord, mapSvc);
            var yearEnd = new YearEndCloseService(
                finSvc,
                lockGuard,
                tenant,
                Repo<FinancialYear>(db),
                Repo<CompanySetting>(db),
                Repo<Core.Domain.Financials.Account>(db),
                Repo<GeneralLedgerSetting>(db),
                Repo<GeneralLedgerLine>(db),
                Repo<JournalEntryHeader>(db),
                Repo<SalesInvoiceHeader>(db),
                Repo<PurchaseInvoiceHeader>(db),
                Repo<Core.Domain.Financials.Bank>(db),
                Repo<BankReconciliation>(db),
                Repo<BankStatementLine>(db),
                Repo<BooksLockChange>(db),
                customers: Repo<Core.Domain.Sales.Customer>(db),
                vendors: Repo<Core.Domain.Purchases.Vendor>(db),
                settlementApplications: Repo<SettlementApplication>(db),
                settlementApplicationReversals: Repo<SettlementApplicationReversal>(db),
                receivableWriteOffs: Repo<ReceivableWriteOff>(db),
                customerCreditNotes: Repo<CustomerCreditNote>(db),
                supplierCreditNotes: Repo<SupplierCreditNote>(db),
                supplierAdvances: Repo<SupplierAdvance>(db),
                partyRefunds: Repo<PartyRefund>(db),
                supplierReturnDispatches: Repo<SupplierReturnDispatch>(db));

            return (conn, db, tenant, custCredits, suppCredits, refunds, writeOffs, returns, yearEnd, stockSvc,
                customer.Id, vendor.Id, bank.Id, item.Id, meas.Id, arAcc.Id, arAltAcc.Id, apAcc.Id, salesAcc.Id, invAcc.Id, cogsAcc.Id, expAcc.Id);
        }

        [Fact]
        public void X09_YearEndReadiness_TiesArApAfterCreditsAdvancesWriteOffs_AndFlagsUnposted()
        {
            var (conn, db, _, custCredits, suppCredits, refunds, writeOffs, _, yearEnd, _,
                custId, vendId, bankId, itemId, measId, arAccId, arAltAccId, apAccId, salesAccId, _, _, expAccId) = CreateFixture(isSolo: true);
            using var _ = conn;
            using var __ = db;

            var invGl = new GeneralLedgerHeader { CompanyId = 1, Date = new DateTime(2026, 5, 1), DocumentType = DocumentTypes.SalesInvoice, Description = "INV-1" };
            invGl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = 1, AccountId = arAccId, DrCr = DrOrCrSide.Dr, Amount = 1000m });
            invGl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = 1, AccountId = salesAccId, DrCr = DrOrCrSide.Cr, Amount = 1000m });
            db.GeneralLedgerHeaders.Add(invGl);
            db.SaveChanges();

            var inv = new SalesInvoiceHeader
            {
                CompanyId = 1, CustomerId = custId, No = "INV-001", Date = new DateTime(2026, 5, 1),
                Status = SalesInvoiceStatus.Open, ReceivableAccountId = arAccId, GeneralLedgerHeaderId = invGl.Id
            };
            inv.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine { CompanyId = 1, ItemId = itemId, MeasurementId = measId, Quantity = 1m, Amount = 1000m });
            db.SalesInvoiceHeaders.Add(inv);
            db.SaveChanges();

            // Change customer's current AR account to arAltAccId to prove CustomerAr honors snapshotted invoice.ReceivableAccountId
            var cust = db.Customers.First(c => c.Id == custId);
            cust.AccountsReceivableAccountId = arAltAccId;
            db.SaveChanges();

            var cn = custCredits.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, custId, null, new DateTime(2026, 6, 1), (int)CreditNoteKind.Standalone, true, "Goodwill", null, null,
                new[] { new CustomerCreditLineInputDto(null, null, null, false, "Goodwill", 1m, 300m, 0m) }));
            var cnPosted = custCredits.Post(cn.Id, new PostCustomerCreditRequest());
            custCredits.Apply(cnPosted.Id, new ApplyCustomerCreditRequest(
                new DateTime(2026, 6, 2), new[] { new ApplyCustomerCreditItemRequest(inv.Id, 300m) }, null, cnPosted.Version));

            var wo = writeOffs.RequestWriteOff(new RequestWriteOffCommand(inv.Id, new DateTime(2026, 7, 1), 700m, 700m, "Uncollectible", "Collection agency"));
            writeOffs.Post(wo.Id, new PostWriteOffCommand(null, wo.Version));

            // Reload invoice and verify IsPaid() == false, DisplayStatus == "Settled" (Finding 5)
            var reloadedInv = db.SalesInvoiceHeaders.Include(i => i.SalesInvoiceLines).Include(i => i.CustomerAllocations).First(i => i.Id == inv.Id);
            Assert.False(reloadedInv.IsPaid());
            Assert.Equal("Settled", reloadedInv.DisplayStatus(new DateTime(2026, 5, 31), new DateTime(2026, 8, 1)));
            Assert.Equal("Settled: cash 0.00, credits 300.00, written off 700.00", reloadedInv.SettlementSummary());

            var billGl = new GeneralLedgerHeader { CompanyId = 1, Date = new DateTime(2026, 5, 5), DocumentType = DocumentTypes.PurchaseInvoice, Description = "BILL-1" };
            billGl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = 1, AccountId = expAccId, DrCr = DrOrCrSide.Dr, Amount = 500m });
            billGl.GeneralLedgerLines.Add(new GeneralLedgerLine { CompanyId = 1, AccountId = apAccId, DrCr = DrOrCrSide.Cr, Amount = 500m });
            db.GeneralLedgerHeaders.Add(billGl);
            db.SaveChanges();

            var bill = new PurchaseInvoiceHeader
            {
                CompanyId = 1, VendorId = vendId, No = "BILL-001", VendorInvoiceNo = "V-001", Date = new DateTime(2026, 5, 5),
                Status = PurchaseInvoiceStatus.Open, PayableAccountId = apAccId, GeneralLedgerHeaderId = billGl.Id
            };
            bill.PurchaseInvoiceLines.Add(new Core.Domain.Purchases.PurchaseInvoiceLine { CompanyId = 1, ItemId = itemId, AccountId = expAccId, Quantity = 1m, Amount = 500m });
            db.PurchaseInvoiceHeaders.Add(bill);
            db.SaveChanges();

            var adv = refunds.SaveAdvanceDraft(new SaveSupplierAdvanceDraftRequest(
                null, vendId, null, new DateTime(2026, 5, 1), 200m, bankId, "Prepayment", true, "WIRE-200", null));
            var advPosted = refunds.PostAdvance(adv.Id, new PostPartyRefundRequest());
            refunds.ApplyAdvance(advPosted.Id, new ApplySupplierAdvanceRequest(
                new DateTime(2026, 5, 10), new[] { new ApplySupplierAdvanceItemRequest(bill.Id, 200m) }));

            var sc = suppCredits.SaveDraft(new SaveSupplierCreditDraftRequest(
                null, vendId, bill.Id, "VCN-300", new DateTime(2026, 5, 15), new DateTime(2026, 5, 15),
                (int)CreditNoteKind.PriceOrServiceAdjustment, false, "Discount", "ACK-300",
                new[] { new SupplierCreditLineInputDto(bill.PurchaseInvoiceLines.First().Id, null, itemId, expAccId, null, false, "Adj", 1m, 300m) }));
            var scPosted = suppCredits.Post(sc.Id, new PostSupplierCreditRequest());
            suppCredits.Apply(scPosted.Id, new ApplySupplierCreditRequest(
                new DateTime(2026, 5, 16), new[] { new ApplySupplierCreditItemRequest(bill.Id, 300m) }, null, scPosted.Version));

            var previewClean = yearEnd.Preview("2026");
            Assert.DoesNotContain(previewClean.Blocking, b => b.Code == "ar_ap_control_tieout_failed");
            Assert.DoesNotContain(previewClean.Blocking, b => b.Code == "unposted_settlement_documents");

            custCredits.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, custId, null, new DateTime(2026, 12, 1), (int)CreditNoteKind.Standalone, true, "Pending", null, null,
                new[] { new CustomerCreditLineInputDto(null, null, null, false, "Pending", 1m, 50m, 0m) }));
            var previewDraft = yearEnd.Preview("2026");
            Assert.Contains(previewDraft.Blocking, b => b.Code == "unposted_settlement_documents");
        }

        [Fact]
        public void X04_S06_C05_ReturnAndRecoveryReversals_StockRebateJournal_AndCumulativeCostResidual()
        {
            var (conn, db, _, _, _, _, writeOffs, returns, _, stockSvc,
                custId, vendId, bankId, itemId, measId, arAccId, _, _, _, _, _, _) = CreateFixture(isSolo: true);
            using var _ = conn;
            using var __ = db;

            var item = db.Items.Include(i => i.InventoryControlJournals).First(i => i.Id == itemId);
            stockSvc.Receive(item, 10m, 10m, DocumentTypes.PurchaseInvoice, 1, new DateTime(2026, 1, 5), "Initial receipt");
            db.SaveChanges();

            // S06: Stock rebate reduces on-hand stock value by $20 AND creates a value-only InventoryControlJournal
            var rebate = returns.PostStockRebate(new PostStockRebateRequest(
                vendId, itemId, new DateTime(2026, 1, 10), "REB-20", 20m, 0m, null, null, "Vendor volume rebate", "REB-20"));
            var itemAfterRebate = db.Items.Include(i => i.InventoryControlJournals).First(i => i.Id == itemId);
            Assert.Equal(80m, itemAfterRebate.StockValue);
            Assert.Equal(8m, itemAfterRebate.AverageCost);
            Assert.Contains(itemAfterRebate.InventoryControlJournals, j => j.DocumentType == DocumentTypes.SupplierCreditNote && j.DocumentId == rebate.Id && j.TotalCost == 20m && j.OUTQty == 0m);

            // C05: Issue 3 units with TotalCost = 10.00 -> return 1 then return 2 -> exact residual 10.00
            var saleMove = new Core.Domain.Items.InventoryControlJournal
            {
                CompanyId = 1, ItemId = itemId, MeasurementId = item.PurchaseMeasurementId ?? 1,
                DocumentType = DocumentTypes.SalesInvoice, DocumentId = 99, Date = new DateTime(2026, 2, 1),
                OUTQty = 3m, UnitCost = 3.3333m, TotalCost = 10.00m, Memo = "Sale 3", IsReverse = false
            };
            db.InventoryControlJournals.Add(saleMove);
            itemAfterRebate.QuantityOnHand = 7m;
            itemAfterRebate.StockValue = 70m;
            db.SaveChanges();

            var inv = new SalesInvoiceHeader { CompanyId = 1, CustomerId = custId, No = "INV-C05", Date = new DateTime(2026, 2, 1), Status = SalesInvoiceStatus.Open, ReceivableAccountId = arAccId };
            var invLine = new Core.Domain.Sales.SalesInvoiceLine { CompanyId = 1, ItemId = itemId, MeasurementId = measId, Quantity = 3m, Amount = 30m, InventoryControlJournalId = saleMove.Id };
            inv.SalesInvoiceLines.Add(invLine);
            db.SalesInvoiceHeaders.Add(inv);
            db.SaveChanges();

            var ret1 = returns.PostCustomerReturn(new PostCustomerReturnRequest(
                inv.Id, null, new DateTime(2026, 2, 5), "Partial 1",
                new[] { new CustomerReturnLineInputDto(invLine.Id, 1m, (int)ReturnCondition.Saleable, null) }));
            Assert.Equal(3.33m, ret1.Lines.First().TotalRestoredCost);

            var ret2 = returns.PostCustomerReturn(new PostCustomerReturnRequest(
                inv.Id, null, new DateTime(2026, 2, 6), "Final 2",
                new[] { new CustomerReturnLineInputDto(invLine.Id, 2m, (int)ReturnCondition.Saleable, null) }));
            Assert.Equal(6.67m, ret2.Lines.First().TotalRestoredCost);

            var revRet2 = returns.ReverseCustomerReturn(ret2.Id, new ReverseSettlementRequest(new DateTime(2026, 2, 7), "Entered in error"));
            Assert.Equal((int)SettlementDocumentStatus.Reversed, revRet2.Status);

            var disp = returns.PostSupplierReturnDispatch(new PostSupplierReturnDispatchRequest(
                vendId, null, null, new DateTime(2026, 3, 1), new DateTime(2026, 3, 1), true, "Defective", "RMA-1",
                new[] { new SupplierReturnLineInputDto(null, null, itemId, 1m) }));
            returns.MatchSupplierReturnCredit(new MatchSupplierReturnCreditRequest(
                disp.Id, "VCN-RMA1", new DateTime(2026, 3, 2), 8m, null, null, "RMA credit", "ACK-RMA1"));
            Assert.Throws<SettlementCommandException>(() =>
                returns.ReverseSupplierReturnDispatch(disp.Id, new ReverseSettlementRequest(new DateTime(2026, 3, 3), "Cannot before credit")));

            var invWo = new SalesInvoiceHeader { CompanyId = 1, CustomerId = custId, No = "INV-WO", Date = new DateTime(2026, 4, 1), Status = SalesInvoiceStatus.Open, ReceivableAccountId = arAccId };
            invWo.SalesInvoiceLines.Add(new Core.Domain.Sales.SalesInvoiceLine { CompanyId = 1, ItemId = itemId, MeasurementId = measId, Quantity = 1m, Amount = 100m });
            db.SalesInvoiceHeaders.Add(invWo);
            db.SaveChanges();

            var wo = writeOffs.RequestWriteOff(new RequestWriteOffCommand(invWo.Id, new DateTime(2026, 4, 2), 100m, 100m, "Bad debt", "Final demand"));
            var woPosted = writeOffs.Post(wo.Id, new PostWriteOffCommand(null, wo.Version));
            var withRec = writeOffs.RecordRecovery(woPosted.Id, new RecordWriteOffRecoveryCommand(new DateTime(2026, 4, 5), 40m, bankId, "REC-1", "Check"));
            Assert.Equal(40m, withRec.RecoveredAmount);
            var recId = withRec.Recoveries.First().Id;
            var afterRevRec = writeOffs.ReverseRecovery(woPosted.Id, recId, new ReverseSettlementRequest(new DateTime(2026, 4, 6), "NSF check"));
            Assert.Equal(0m, afterRevRec.RecoveredAmount);
            var afterRevWo = writeOffs.Reverse(woPosted.Id, new ReverseSettlementRequest(new DateTime(2026, 4, 7), "Customer paid"));
            Assert.Equal((int)SettlementDocumentStatus.Reversed, afterRevWo.Status);
        }

        [Fact]
        public void TeamSecurity_BlocksDirectDraftPost_CrossDirectionRefund_AndMissingBankPicker()
        {
            var mockAuthDenied = new Moq.Mock<IAccessAuthorizationService>();
            mockAuthDenied.Setup(a => a.AuthorizeRequirement(
                    Moq.It.IsAny<IReadOnlyList<string>?>(), Moq.It.IsAny<IReadOnlyList<string>?>(),
                    Moq.It.IsAny<IReadOnlyList<CompanyMemberRole>?>(), Moq.It.IsAny<IReadOnlyList<string>?>(),
                    Moq.It.IsAny<string?>(), Moq.It.IsAny<AccessResourceContext?>(),
                    Moq.It.IsAny<AccessActorContext?>(), AccessOperationCatalog.BankingAccountsPicker))
                .Returns(AccessDecision.Deny(AccessReasonCode.PermissionDenied, AccessOperationCatalog.BankingAccountsPicker,
                    "POL-BANK", "1.0", 403, "Forbidden", "Missing banking picker access.", SystemPermissions.BankingAccountsView));

            var (conn, db, _, custCredits, suppCredits, refunds, _, _, _, _,
                custId, vendId, bankId, _, _, _, _, _, _, _, _, expAccId) = CreateFixture(isSolo: false, auth: mockAuthDenied.Object);
            using var _ = conn;
            using var __ = db;

            var cnDraft = custCredits.SaveDraft(new SaveCustomerCreditDraftRequest(
                null, custId, null, new DateTime(2026, 5, 1), (int)CreditNoteKind.Standalone, true, "Goodwill", null, null,
                new[] { new CustomerCreditLineInputDto(null, null, null, false, "Goodwill", 1m, 50m, 0m) }));
            var cnEx = Assert.Throws<SettlementCommandException>(() => custCredits.Post(cnDraft.Id, new PostCustomerCreditRequest()));
            Assert.Equal(SettlementErrorCodes.SelfApprovalForbidden, cnEx.Code);

            var scDraft = suppCredits.SaveDraft(new SaveSupplierCreditDraftRequest(
                null, vendId, null, "V-1", new DateTime(2026, 5, 1), new DateTime(2026, 5, 1),
                (int)CreditNoteKind.Standalone, true, "Adj", "ACK",
                new[] { new SupplierCreditLineInputDto(null, null, null, expAccId, null, false, "Adj", 1m, 50m) }));
            var scEx = Assert.Throws<SettlementCommandException>(() => suppCredits.Post(scDraft.Id, new PostSupplierCreditRequest()));
            Assert.Equal(SettlementErrorCodes.SelfApprovalForbidden, scEx.Code);

            var bankEx = Assert.Throws<SettlementCommandException>(() => refunds.SaveAdvanceDraft(
                new SaveSupplierAdvanceDraftRequest(null, vendId, null, new DateTime(2026, 5, 1), 100m, bankId, "Deposit", true, null, null)));
            Assert.Equal(SettlementErrorCodes.PermissionDenied, bankEx.Code);
            Assert.Equal(403, bankEx.HttpStatus);

            var dirEx = Assert.Throws<SettlementCommandException>(() => refunds.SaveRefundDraft(
                new SavePartyRefundDraftRequest(null, (int)PartyRefundDirection.SupplierInflow, null, vendId,
                    new DateTime(2026, 5, 1), bankId, "Bank Transfer", "EXT-1", "Evidence",
                    new[] { new RefundAllocationInputDto((int)SettlementSourceType.SupplierCreditNote, scDraft.Id, 10m) }),
                expectedDirection: PartyRefundDirection.CustomerPayout));
            Assert.Equal(SettlementErrorCodes.PermissionDenied, dirEx.Code);
            Assert.Equal(403, dirEx.HttpStatus);
        }
    }
}

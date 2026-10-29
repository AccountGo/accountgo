using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Items;
using Core.Domain.TaxSystem;
using Moq;
using Services.Financial;
using Services.Inventory;
using Services.TaxSystem;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class CashFlowStatementTests
    {
        private static FinancialService CreateFinancialService(
            IRepository<Account> accountRepo,
            IRepository<GeneralLedgerHeader> glHeaderRepo,
            IRepository<GeneralLedgerLine> glLineRepo,
            IRepository<GeneralLedgerSetting> glSettingRepo = null)
        {
            return new FinancialService(
                Mock.Of<IInventoryService>(),
                Mock.Of<ITaxService>(),
                glHeaderRepo,
                glLineRepo,
                accountRepo,
                Mock.Of<IRepository<AccountClass>>(),
                Mock.Of<IRepository<Tax>>(),
                Mock.Of<IRepository<JournalEntryHeader>>(),
                Mock.Of<IRepository<JournalEntryLine>>(),
                Mock.Of<IRepository<FinancialYear>>(),
                Mock.Of<IRepository<TaxGroup>>(),
                Mock.Of<IRepository<ItemTaxGroup>>(),
                Mock.Of<IRepository<PaymentTerm>>(),
                Mock.Of<IRepository<Bank>>(),
                Mock.Of<IRepository<Item>>(),
                glSettingRepo ?? Mock.Of<IRepository<GeneralLedgerSetting>>(),
                Mock.Of<IRepository<CompanySetting>>());
        }

        [Fact]
        public void CashFlow_AccrualBasis_ReconcilesNetIncomeAndCashBalances_WithSamplePeriodActivity()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var ar = new Account { Id = 2, AccountCode = "10120", AccountName = "Accounts Receivable", AccountClassId = 1, IsCash = false };
            var accumDeprec = new Account { Id = 3, AccountCode = "14001", AccountName = "Accumulated Depreciation", AccountClassId = 1, IsContraAccount = true };
            var equipment = new Account { Id = 4, AccountCode = "13302", AccountName = "Office Equipment", AccountClassId = 1, IsCash = false };
            var ap = new Account { Id = 5, AccountCode = "20110", AccountName = "Accounts Payable", AccountClassId = 2 };
            var memberCapital = new Account { Id = 6, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };
            var sales = new Account { Id = 7, AccountCode = "40100", AccountName = "Sales Revenue", AccountClassId = 4 };
            var opExpense = new Account { Id = 8, AccountCode = "50100", AccountName = "Operating Expense", AccountClassId = 5 };
            var deprecExpense = new Account { Id = 9, AccountCode = "50116", AccountName = "Depreciation Expense", AccountClassId = 5 };

            var accounts = new List<Account> { cash, ar, accumDeprec, equipment, ap, memberCapital, sales, opExpense, deprecExpense };

            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var mockGlRepo = new Mock<IRepository<GeneralLedgerHeader>>();
            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();

            var startDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1;
            int lineId = 1;

            void Post(DateTime date, (Account Account, DrOrCrSide Side, decimal Amount) l1, (Account Account, DrOrCrSide Side, decimal Amount) l2)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l1.Account, AccountId = l1.Account.Id, DrCr = l1.Side, Amount = l1.Amount });
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l2.Account, AccountId = l2.Account.Id, DrCr = l2.Side, Amount = l2.Amount });
            }

            // Prior activity (Opening cash = 1000)
            Post(startDate.AddDays(-5), (cash, DrOrCrSide.Dr, 1000m), (memberCapital, DrOrCrSide.Cr, 1000m));

            // In-period activity:
            // 1. Credit sale: Dr AR 500, Cr Sales 500
            Post(startDate.AddDays(1), (ar, DrOrCrSide.Dr, 500m), (sales, DrOrCrSide.Cr, 500m));
            // 2. AR collection: Dr Cash 300, Cr AR 300
            Post(startDate.AddDays(2), (cash, DrOrCrSide.Dr, 300m), (ar, DrOrCrSide.Cr, 300m));
            // 3. Purchase on credit: Dr Expense 200, Cr AP 200
            Post(startDate.AddDays(3), (opExpense, DrOrCrSide.Dr, 200m), (ap, DrOrCrSide.Cr, 200m));
            // 4. AP payment: Dr AP 150, Cr Cash 150
            Post(startDate.AddDays(4), (ap, DrOrCrSide.Dr, 150m), (cash, DrOrCrSide.Cr, 150m));
            // 5. Depreciation (non-cash): Dr Deprec Expense 50, Cr Accum Deprec 50
            Post(startDate.AddDays(5), (deprecExpense, DrOrCrSide.Dr, 50m), (accumDeprec, DrOrCrSide.Cr, 50m));
            // 6. Equipment purchase (investing): Dr Equipment 400, Cr Cash 400
            Post(startDate.AddDays(6), (equipment, DrOrCrSide.Dr, 400m), (cash, DrOrCrSide.Cr, 400m));
            // 7. Capital contribution (financing): Dr Cash 200, Cr Member Capital 200
            Post(startDate.AddDays(7), (cash, DrOrCrSide.Dr, 200m), (memberCapital, DrOrCrSide.Cr, 200m));

            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());

            var financialService = CreateFinancialService(mockAccountRepo.Object, mockGlRepo.Object, mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.NotNull(report);
            Assert.Null(report.Message);
            Assert.Equal(AccountingBasis.Accrual, report.AccountingBasis);

            // Starting cash = 1000m
            Assert.Equal(1000m, report.StartingCash);

            // Net Income = 500 (Sales) - 200 (OpExpense) - 50 (DeprecExpense) = 250m
            Assert.Equal(250m, report.NetIncome);

            // Operating Activities:
            // Depreciation add-back: +50m
            // Δ AR: Cr 300 - Dr 500 = -200m
            // Δ AP: Cr 200 - Dr 150 = +50m
            // Total Operating Cash Flow = 250 + 50 - 200 + 50 = 150m (Collected 300 - Paid 150 = 150)
            Assert.Equal(150m, report.OperatingCashFlow);
            var deprecLine = Assert.Single(report.OperatingActivities, a => a.AccountCode == "14001");
            Assert.Equal(50m, deprecLine.Amount);
            Assert.Equal("NonCashAdjustment", deprecLine.LineType);
            var arLine = Assert.Single(report.OperatingActivities, a => a.AccountCode == "10120");
            Assert.Equal(-200m, arLine.Amount);
            Assert.Equal("WorkingCapital", arLine.LineType);
            var apLine = Assert.Single(report.OperatingActivities, a => a.AccountCode == "20110");
            Assert.Equal(50m, apLine.Amount);
            Assert.Equal("WorkingCapital", apLine.LineType);

            // Investing Activities:
            // Equipment: Cr 0 - Dr 400 = -400m
            Assert.Equal(-400m, report.InvestingCashFlow);
            var equipLine = Assert.Single(report.InvestingActivities, a => a.AccountCode == "13302");
            Assert.Equal(-400m, equipLine.Amount);
            Assert.Equal("Investing", equipLine.LineType);

            // Financing Activities:
            // Member Capital: Cr 200 - Dr 0 = +200m
            Assert.Equal(200m, report.FinancingCashFlow);
            var capitalLine = Assert.Single(report.FinancingActivities, a => a.AccountCode == "30100");
            Assert.Equal(200m, capitalLine.Amount);
            Assert.Equal("Financing", capitalLine.LineType);

            // Net Change in Cash = Operating (150) + Investing (-400) + Financing (200) = -50m
            Assert.Equal(-50m, report.NetChangeInCash);

            // Ending Cash = Starting Cash (1000) + Net Change (-50) = 950m
            Assert.Equal(950m, report.EndingCash);
            Assert.Equal(report.EndingCash - report.StartingCash, report.NetChangeInCash);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_CashBasis_ReturnsInformativeMessageAndReconcilesCashBalances()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var memberCapital = new Account { Id = 6, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            var accounts = new List<Account> { cash, memberCapital };

            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var mockGlRepo = new Mock<IRepository<GeneralLedgerHeader>>();
            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();

            var startDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 4, 30, 23, 59, 59, DateTimeKind.Utc);

            var header1 = new GeneralLedgerHeader { Id = 1, Date = startDate.AddDays(-1), CompanyId = 1 };
            var header2 = new GeneralLedgerHeader { Id = 2, Date = startDate.AddDays(5), CompanyId = 1 };

            var glLines = new List<GeneralLedgerLine>
            {
                new GeneralLedgerLine { Id = 1, GeneralLedgerHeader = header1, GeneralLedgerHeaderId = 1, Account = cash, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 500m },
                new GeneralLedgerLine { Id = 2, GeneralLedgerHeader = header1, GeneralLedgerHeaderId = 1, Account = memberCapital, AccountId = memberCapital.Id, DrCr = DrOrCrSide.Cr, Amount = 500m },
                new GeneralLedgerLine { Id = 3, GeneralLedgerHeader = header2, GeneralLedgerHeaderId = 2, Account = cash, AccountId = cash.Id, DrCr = DrOrCrSide.Dr, Amount = 250m },
                new GeneralLedgerLine { Id = 4, GeneralLedgerHeader = header2, GeneralLedgerHeaderId = 2, Account = memberCapital, AccountId = memberCapital.Id, DrCr = DrOrCrSide.Cr, Amount = 250m },
            };

            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());

            var financialService = CreateFinancialService(mockAccountRepo.Object, mockGlRepo.Object, mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Cash);

            Assert.NotNull(report);
            Assert.NotNull(report.Message);
            Assert.Contains("designed for Accrual basis", report.Message);
            Assert.Equal(AccountingBasis.Cash, report.AccountingBasis);
            Assert.Equal(500m, report.StartingCash);
            Assert.Equal(750m, report.EndingCash);
            Assert.Equal(250m, report.NetChangeInCash);
            Assert.Empty(report.OperatingActivities);
            Assert.Empty(report.InvestingActivities);
            Assert.Empty(report.FinancingActivities);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_ClosedToRetainedEarnings_ClassifiesClosingAsOperating_NotFinancing()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var retainedSurplus = new Account { Id = 2, AccountCode = "30300", AccountName = "Retained Surplus", AccountClassId = 3 };
            var memberCapital = new Account { Id = 3, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };
            var sales = new Account { Id = 4, AccountCode = "40100", AccountName = "Sales Revenue", AccountClassId = 4 };
            var expense = new Account { Id = 5, AccountCode = "50100", AccountName = "Operating Expense", AccountClassId = 5 };

            var accounts = new List<Account> { cash, retainedSurplus, memberCapital, sales, expense };

            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var mockGlSettingRepo = new Mock<IRepository<GeneralLedgerSetting>>();
            mockGlSettingRepo.Setup(r => r.Table).Returns(new List<GeneralLedgerSetting>
            {
                new GeneralLedgerSetting { PermanentAccountId = retainedSurplus.Id }
            }.AsQueryable());

            var mockGlRepo = new Mock<IRepository<GeneralLedgerHeader>>();
            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();

            var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, (Account Account, DrOrCrSide Side, decimal Amount) l1, (Account Account, DrOrCrSide Side, decimal Amount) l2)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l1.Account, AccountId = l1.Account.Id, DrCr = l1.Side, Amount = l1.Amount });
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l2.Account, AccountId = l2.Account.Id, DrCr = l2.Side, Amount = l2.Amount });
            }

            // Opening capital: Dr Cash 1000, Cr Member Capital 1000 (prior period)
            Post(startDate.AddDays(-10), (cash, DrOrCrSide.Dr, 1000m), (memberCapital, DrOrCrSide.Cr, 1000m));

            // Operations in period:
            // Cash sales: 800
            Post(startDate.AddDays(20), (cash, DrOrCrSide.Dr, 800m), (sales, DrOrCrSide.Cr, 800m));
            // Cash expense: 300
            Post(startDate.AddDays(30), (expense, DrOrCrSide.Dr, 300m), (cash, DrOrCrSide.Cr, 300m));

            // Closing entry at year-end: close Sales (800) and Expense (300) to Retained Surplus (500)
            var closeHeader = new GeneralLedgerHeader { Id = headerId++, Date = endDate.AddHours(-1), CompanyId = 1 };
            glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = closeHeader, GeneralLedgerHeaderId = closeHeader.Id, Account = sales, AccountId = sales.Id, DrCr = DrOrCrSide.Dr, Amount = 800m });
            glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = closeHeader, GeneralLedgerHeaderId = closeHeader.Id, Account = expense, AccountId = expense.Id, DrCr = DrOrCrSide.Cr, Amount = 300m });
            glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = closeHeader, GeneralLedgerHeaderId = closeHeader.Id, Account = retainedSurplus, AccountId = retainedSurplus.Id, DrCr = DrOrCrSide.Cr, Amount = 500m });

            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());

            var financialService = CreateFinancialService(mockAccountRepo.Object, mockGlRepo.Object, mockGlLineRepo.Object, mockGlSettingRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.NotNull(report);
            Assert.Equal(1000m, report.StartingCash);
            Assert.Equal(1500m, report.EndingCash);
            Assert.Equal(500m, report.NetChangeInCash);

            // After year-end closing, P&L accounts net to 0
            Assert.Equal(0m, report.NetIncome);

            // Retained Surplus closing movement is classified under Operating Activities, NOT Financing Activities
            Assert.Equal(500m, report.OperatingCashFlow);
            var reLine = Assert.Single(report.OperatingActivities, a => a.AccountCode == "30300");
            Assert.Equal(500m, reLine.Amount);
            Assert.Equal("Operating", reLine.LineType);

            Assert.Equal(0m, report.InvestingCashFlow);
            Assert.Equal(0m, report.FinancingCashFlow);
            Assert.Empty(report.FinancingActivities);

            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_StandardOperatingSaleAndOtherAssets_OnDefaultCoaHierarchy()
        {
            var topAsset = new Account { Id = 100, AccountCode = "10000", AccountName = "Assets", AccountClassId = 1 };
            var currentAsset = new Account { Id = 101, AccountCode = "10100", AccountName = "Current Assets", AccountClassId = 1, ParentAccountId = 100 };
            var cashParent = new Account { Id = 102, AccountCode = "10110", AccountName = "Cash in Hand and in Bank", AccountClassId = 1, ParentAccountId = 101, IsCash = true };
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Regular Checking Account", AccountClassId = 1, ParentAccountId = 102, IsCash = false };
            var ar = new Account { Id = 2, AccountCode = "10120", AccountName = "Accounts Receivable", AccountClassId = 1, ParentAccountId = 101 };
            var otherAssets = new Account { Id = 3, AccountCode = "15000", AccountName = "Other Assets", AccountClassId = 1, ParentAccountId = 100 };
            var sales = new Account { Id = 4, AccountCode = "40100", AccountName = "Sales A/C", AccountClassId = 4 };

            var accounts = new List<Account> { topAsset, currentAsset, cashParent, cash, ar, otherAssets, sales };

            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var mockGlRepo = new Mock<IRepository<GeneralLedgerHeader>>();
            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();

            var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, (Account Account, DrOrCrSide Side, decimal Amount) l1, (Account Account, DrOrCrSide Side, decimal Amount) l2)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l1.Account, AccountId = l1.Account.Id, DrCr = l1.Side, Amount = l1.Amount });
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l2.Account, AccountId = l2.Account.Id, DrCr = l2.Side, Amount = l2.Amount });
            }

            // 1. Credit sale: Dr AR 1000, Cr Sales 1000
            Post(startDate.AddDays(5), (ar, DrOrCrSide.Dr, 1000m), (sales, DrOrCrSide.Cr, 1000m));
            // 2. Cash collection: Dr Cash 600, Cr AR 600
            Post(startDate.AddDays(10), (cash, DrOrCrSide.Dr, 600m), (ar, DrOrCrSide.Cr, 600m));
            // 3. Operating purchase into Other Assets: Dr Other Assets (15000) 100, Cr Cash 100
            Post(startDate.AddDays(15), (otherAssets, DrOrCrSide.Dr, 100m), (cash, DrOrCrSide.Cr, 100m));

            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());

            var financialService = CreateFinancialService(mockAccountRepo.Object, mockGlRepo.Object, mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.NotNull(report);
            Assert.Equal(0m, report.StartingCash);
            Assert.Equal(500m, report.EndingCash);
            Assert.Equal(500m, report.NetChangeInCash);

            // Net Income = 1000m
            Assert.Equal(1000m, report.NetIncome);

            // Operating Activities:
            // Δ AR: -400m
            // Δ Other Assets (15000): -100m (in Operating Working Capital, NOT Investing!)
            // Total Operating Cash Flow = 1000 - 400 - 100 = 500m
            Assert.Equal(500m, report.OperatingCashFlow);
            var otherAssetLine = Assert.Single(report.OperatingActivities, a => a.AccountCode == "15000");
            Assert.Equal(-100m, otherAssetLine.Amount);
            Assert.Equal("WorkingCapital", otherAssetLine.LineType);

            Assert.Equal(0m, report.InvestingCashFlow);
            Assert.Empty(report.InvestingActivities);
            Assert.Equal(0m, report.FinancingCashFlow);
            Assert.Empty(report.FinancingActivities);

            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_DividendFromRetainedEarnings_IsFinancing_NotOperating()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var retainedSurplus = new Account { Id = 2, AccountCode = "30300", AccountName = "Retained Surplus", AccountClassId = 3 };
            var memberCapital = new Account { Id = 3, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            var accounts = new List<Account> { cash, retainedSurplus, memberCapital };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var mockGlSettingRepo = new Mock<IRepository<GeneralLedgerSetting>>();
            mockGlSettingRepo.Setup(r => r.Table).Returns(new List<GeneralLedgerSetting>
            {
                new GeneralLedgerSetting { PermanentAccountId = retainedSurplus.Id }
            }.AsQueryable());

            var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, (Account Account, DrOrCrSide Side, decimal Amount) l1, (Account Account, DrOrCrSide Side, decimal Amount) l2)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l1.Account, AccountId = l1.Account.Id, DrCr = l1.Side, Amount = l1.Amount });
                glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = l2.Account, AccountId = l2.Account.Id, DrCr = l2.Side, Amount = l2.Amount });
            }

            Post(startDate.AddDays(-5), (cash, DrOrCrSide.Dr, 1000m), (memberCapital, DrOrCrSide.Cr, 1000m));
            // Cash dividend: Dr Retained Surplus 200, Cr Cash 200
            Post(startDate.AddDays(40), (retainedSurplus, DrOrCrSide.Dr, 200m), (cash, DrOrCrSide.Cr, 200m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());

            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object,
                mockGlSettingRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.Equal(1000m, report.StartingCash);
            Assert.Equal(800m, report.EndingCash);
            Assert.Equal(-200m, report.NetChangeInCash);
            Assert.Equal(0m, report.NetIncome);
            Assert.Equal(0m, report.OperatingCashFlow);
            Assert.DoesNotContain(report.OperatingActivities, a => a.AccountCode == "30300");
            Assert.Equal(-200m, report.FinancingCashFlow);
            var dividendLine = Assert.Single(report.FinancingActivities, a => a.AccountCode == "30300");
            Assert.Equal(-200m, dividendLine.Amount);
            Assert.Equal(CashFlowLineTypes.Financing, dividendLine.LineType);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_ClosingAndDividendInSamePeriod_SplitsOperatingAndFinancing()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var retainedSurplus = new Account { Id = 2, AccountCode = "30300", AccountName = "Retained Surplus", AccountClassId = 3 };
            var sales = new Account { Id = 4, AccountCode = "40100", AccountName = "Sales Revenue", AccountClassId = 4 };
            var expense = new Account { Id = 5, AccountCode = "50100", AccountName = "Operating Expense", AccountClassId = 5 };

            var accounts = new List<Account> { cash, retainedSurplus, sales, expense };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());
            var mockGlSettingRepo = new Mock<IRepository<GeneralLedgerSetting>>();
            mockGlSettingRepo.Setup(r => r.Table).Returns(new List<GeneralLedgerSetting>
            {
                new GeneralLedgerSetting { PermanentAccountId = retainedSurplus.Id }
            }.AsQueryable());

            var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, params (Account Account, DrOrCrSide Side, decimal Amount)[] lines)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                foreach (var line in lines)
                    glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = line.Account, AccountId = line.Account.Id, DrCr = line.Side, Amount = line.Amount });
            }

            Post(startDate.AddDays(-1), (cash, DrOrCrSide.Dr, 500m), (retainedSurplus, DrOrCrSide.Cr, 500m));
            Post(startDate.AddDays(10), (cash, DrOrCrSide.Dr, 400m), (sales, DrOrCrSide.Cr, 400m));
            Post(startDate.AddDays(20), (expense, DrOrCrSide.Dr, 100m), (cash, DrOrCrSide.Cr, 100m));
            // Close NI 300 to RE
            Post(endDate.AddHours(-2),
                (sales, DrOrCrSide.Dr, 400m),
                (expense, DrOrCrSide.Cr, 100m),
                (retainedSurplus, DrOrCrSide.Cr, 300m));
            // Dividend 50 from RE
            Post(endDate.AddHours(-1), (retainedSurplus, DrOrCrSide.Dr, 50m), (cash, DrOrCrSide.Cr, 50m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());
            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object,
                mockGlSettingRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.Equal(0m, report.NetIncome);
            Assert.Equal(300m, report.OperatingCashFlow);
            Assert.Equal(-50m, report.FinancingCashFlow);
            Assert.Equal(250m, report.NetChangeInCash);
            Assert.Equal(500m, report.StartingCash);
            Assert.Equal(750m, report.EndingCash);
            Assert.Contains(report.OperatingActivities, a => a.AccountCode == "30300" && a.Amount == 300m);
            Assert.Contains(report.FinancingActivities, a => a.AccountCode == "30300" && a.Amount == -50m);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_GainOnAssetDisposal_ReclassifiedFromOperatingToInvesting()
        {
            var noncurrent = new Account { Id = 10, AccountCode = "10200", AccountName = "Noncurrent Assets", AccountClassId = 1 };
            var fixedAssets = new Account { Id = 11, AccountCode = "10300", AccountName = "Fixed Assets", AccountClassId = 1, ParentAccountId = 10 };
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var equipment = new Account { Id = 2, AccountCode = "13302", AccountName = "Office Equipment", AccountClassId = 1, ParentAccountId = 11 };
            var gainOnSale = new Account { Id = 3, AccountCode = "40200", AccountName = "Gain on Sale of Assets", AccountClassId = 4 };
            var capital = new Account { Id = 4, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            var accounts = new List<Account> { noncurrent, fixedAssets, cash, equipment, gainOnSale, capital };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, params (Account Account, DrOrCrSide Side, decimal Amount)[] lines)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                foreach (var line in lines)
                    glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = line.Account, AccountId = line.Account.Id, DrCr = line.Side, Amount = line.Amount });
            }

            Post(startDate.AddDays(-10), (equipment, DrOrCrSide.Dr, 400m), (capital, DrOrCrSide.Cr, 400m));
            // Sell equipment NBV 400 for cash 500 → gain 100
            Post(startDate.AddDays(5),
                (cash, DrOrCrSide.Dr, 500m),
                (equipment, DrOrCrSide.Cr, 400m),
                (gainOnSale, DrOrCrSide.Cr, 100m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());
            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.Equal(100m, report.NetIncome);
            // Gain removed from operating; cash proceeds fully in investing
            Assert.Equal(0m, report.OperatingCashFlow);
            Assert.Contains(report.OperatingActivities, a => a.LineType == CashFlowLineTypes.NonCashAdjustment && a.Amount == -100m);
            Assert.Equal(500m, report.InvestingCashFlow);
            Assert.Equal(500m, report.NetChangeInCash);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_LossOnAssetDisposal_AddedBackInOperating_ProceedsInInvesting()
        {
            var noncurrent = new Account { Id = 10, AccountCode = "10200", AccountName = "Noncurrent Assets", AccountClassId = 1 };
            var fixedAssets = new Account { Id = 11, AccountCode = "10300", AccountName = "Fixed Assets", AccountClassId = 1, ParentAccountId = 10 };
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var equipment = new Account { Id = 2, AccountCode = "13302", AccountName = "Office Equipment", AccountClassId = 1, ParentAccountId = 11 };
            var lossOnSale = new Account { Id = 3, AccountCode = "50900", AccountName = "Loss on Disposal of Assets", AccountClassId = 5 };
            var capital = new Account { Id = 4, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            var accounts = new List<Account> { noncurrent, fixedAssets, cash, equipment, lossOnSale, capital };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var startDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 3, 31, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, params (Account Account, DrOrCrSide Side, decimal Amount)[] lines)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                foreach (var line in lines)
                    glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = line.Account, AccountId = line.Account.Id, DrCr = line.Side, Amount = line.Amount });
            }

            Post(startDate.AddDays(-10), (equipment, DrOrCrSide.Dr, 400m), (capital, DrOrCrSide.Cr, 400m));
            // Sell NBV 400 for cash 350 → loss 50
            Post(startDate.AddDays(5),
                (cash, DrOrCrSide.Dr, 350m),
                (lossOnSale, DrOrCrSide.Dr, 50m),
                (equipment, DrOrCrSide.Cr, 400m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());
            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.Equal(-50m, report.NetIncome);
            Assert.Equal(0m, report.OperatingCashFlow);
            Assert.Contains(report.OperatingActivities, a => a.LineType == CashFlowLineTypes.NonCashAdjustment && a.Amount == 50m);
            Assert.Equal(350m, report.InvestingCashFlow);
            Assert.Equal(350m, report.NetChangeInCash);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_DisposalWithAccumulatedDepreciation_NetsInvestingToCashProceeds()
        {
            var noncurrent = new Account { Id = 10, AccountCode = "10200", AccountName = "Noncurrent Assets", AccountClassId = 1 };
            var fixedAssets = new Account { Id = 11, AccountCode = "10300", AccountName = "Fixed Assets", AccountClassId = 1, ParentAccountId = 10 };
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var equipment = new Account { Id = 2, AccountCode = "13302", AccountName = "Office Equipment", AccountClassId = 1, ParentAccountId = 11 };
            var accumDep = new Account { Id = 3, AccountCode = "14002", AccountName = "Accumulated Depreciation-Equipment", AccountClassId = 1, ParentAccountId = 11, IsContraAccount = true };
            var gainOnSale = new Account { Id = 4, AccountCode = "40200", AccountName = "Gain on Sale of Assets", AccountClassId = 4 };
            var depExpense = new Account { Id = 5, AccountCode = "50116", AccountName = "Depreciation Expense", AccountClassId = 5 };
            var capital = new Account { Id = 6, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            var accounts = new List<Account> { noncurrent, fixedAssets, cash, equipment, accumDep, gainOnSale, depExpense, capital };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var startDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, params (Account Account, DrOrCrSide Side, decimal Amount)[] lines)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                foreach (var line in lines)
                    glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = line.Account, AccountId = line.Account.Id, DrCr = line.Side, Amount = line.Amount });
            }

            // Prior: buy equipment 1000; prior depreciation 600 (opening accum)
            Post(startDate.AddDays(-20), (equipment, DrOrCrSide.Dr, 1000m), (capital, DrOrCrSide.Cr, 1000m));
            Post(startDate.AddDays(-10), (depExpense, DrOrCrSide.Dr, 600m), (accumDep, DrOrCrSide.Cr, 600m));
            // In period: depreciation 50, then dispose NBV 350 for cash 450 → gain 100
            // Opening NBV at period start = 1000-600 = 400; after period dep 50, NBV 350; sell 450 → gain 100
            Post(startDate.AddDays(2), (depExpense, DrOrCrSide.Dr, 50m), (accumDep, DrOrCrSide.Cr, 50m));
            Post(startDate.AddDays(10),
                (cash, DrOrCrSide.Dr, 450m),
                (accumDep, DrOrCrSide.Dr, 650m),
                (equipment, DrOrCrSide.Cr, 1000m),
                (gainOnSale, DrOrCrSide.Cr, 100m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());
            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            // NI = gain 100 - dep expense 50 = 50
            Assert.Equal(50m, report.NetIncome);
            // Operating: NI 50 + dep add-back 50 + remove gain (-100) = 0
            Assert.Equal(0m, report.OperatingCashFlow);
            Assert.Contains(report.OperatingActivities, a => a.AccountCode == "14002" && a.Amount == 50m && a.LineType == CashFlowLineTypes.NonCashAdjustment);
            Assert.DoesNotContain(report.OperatingActivities, a => a.AccountCode == "14002" && a.Amount == -650m);
            // Investing = cash proceeds 450 (asset/accum disposal + gain reclass)
            Assert.Equal(450m, report.InvestingCashFlow);
            Assert.Equal(450m, report.NetChangeInCash);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_AccrualBasis_InventoryGainOnSale_StaysInOperating_NotReclassifiedToInvesting()
        {
            var cash = new Account { Id = 1, AccountCode = "10111", AccountName = "Cash in Bank", AccountClassId = 1, IsCash = true };
            var inventory = new Account { Id = 2, AccountCode = "10800", AccountName = "Inventory", AccountClassId = 1 };
            var gainOnSale = new Account { Id = 3, AccountCode = "40250", AccountName = "Gain on Sale of Inventory", AccountClassId = 4 };
            var capital = new Account { Id = 4, AccountCode = "30100", AccountName = "Member Capital", AccountClassId = 3 };

            // Put inventory under current assets hierarchy
            var current = new Account { Id = 10, AccountCode = "10100", AccountName = "Current Assets", AccountClassId = 1 };
            inventory.ParentAccountId = 10;

            var accounts = new List<Account> { cash, inventory, gainOnSale, capital, current };
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(accounts.AsQueryable());

            var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var endDate = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
            var glLines = new List<GeneralLedgerLine>();
            int headerId = 1, lineId = 1;

            void Post(DateTime date, params (Account Account, DrOrCrSide Side, decimal Amount)[] lines)
            {
                var header = new GeneralLedgerHeader { Id = headerId++, Date = date, CompanyId = 1 };
                foreach (var line in lines)
                    glLines.Add(new GeneralLedgerLine { Id = lineId++, GeneralLedgerHeader = header, GeneralLedgerHeaderId = header.Id, Account = line.Account, AccountId = line.Account.Id, DrCr = line.Side, Amount = line.Amount });
            }

            Post(startDate.AddDays(-1), (inventory, DrOrCrSide.Dr, 80m), (capital, DrOrCrSide.Cr, 80m));
            Post(startDate.AddDays(3),
                (cash, DrOrCrSide.Dr, 100m),
                (inventory, DrOrCrSide.Cr, 80m),
                (gainOnSale, DrOrCrSide.Cr, 20m));

            var mockGlLineRepo = new Mock<IRepository<GeneralLedgerLine>>();
            mockGlLineRepo.Setup(r => r.Table).Returns(glLines.AsQueryable());
            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                mockGlLineRepo.Object);

            var report = financialService.CashFlow(startDate, endDate, AccountingBasis.Accrual);

            Assert.Equal(20m, report.NetIncome);
            // Inventory Δ and gain remain operating — not shifted to investing
            Assert.Equal(100m, report.OperatingCashFlow);
            Assert.Equal(0m, report.InvestingCashFlow);
            Assert.DoesNotContain(report.OperatingActivities, a => a.AccountCode == "40250");
            Assert.DoesNotContain(report.InvestingActivities, a => a.AccountCode == "40250");
            Assert.Equal(100m, report.NetChangeInCash);
            Assert.True(report.IsBalanced);
        }

        [Fact]
        public void CashFlow_ThrowsWhenFromDateAfterToDate()
        {
            var mockAccountRepo = new Mock<IRepository<Account>>();
            mockAccountRepo.Setup(r => r.Table).Returns(new List<Account>().AsQueryable());

            var financialService = CreateFinancialService(
                mockAccountRepo.Object,
                new Mock<IRepository<GeneralLedgerHeader>>().Object,
                new Mock<IRepository<GeneralLedgerLine>>().Object);

            var ex = Assert.Throws<ArgumentException>(() =>
                financialService.CashFlow(new DateTime(2026, 5, 1), new DateTime(2026, 4, 1)));

            Assert.Contains("start date must not be after the end date", ex.Message);
        }
    }
}

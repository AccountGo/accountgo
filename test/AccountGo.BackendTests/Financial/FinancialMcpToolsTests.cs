#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Mcp.Models;
using Api.Mcp.Tools;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Security;
using Core.Tenancy;
using Dto.Financial;
using Dto.Subscription;
using Moq;
using Services.Administration;
using Services.Financial;
using Xunit;
using AccountClass = Core.Domain.Financials.AccountClass;

namespace AccountGo.BackendTests.Financial
{
    public class FinancialMcpToolsTests
    {
        private readonly Mock<IFinancialService> _mockFinancial = new();
        private readonly Mock<IAdministrationService> _mockAdmin = new();
        private readonly Mock<ITenantContext> _mockTenant = new();

        public FinancialMcpToolsTests()
        {
            _mockTenant.Setup(t => t.IsResolved).Returns(true);
            _mockTenant.Setup(t => t.CompanyId).Returns(1);
            _mockTenant.Setup(t => t.CompanySlug).Returns("ph-financials");
            _mockTenant.Setup(t => t.Role).Returns(CompanyMemberRole.Owner);

            _mockAdmin.Setup(a => a.IsModuleEnabled("financials")).Returns(true);
            _mockAdmin.Setup(a => a.IsModuleEnabled("reports")).Returns(true);
            _mockAdmin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto
            {
                CompanyId = 1,
                CompanyName = "PH Financials",
                Slug = "ph-financials",
                Plan = "accounting",
                SubscriptionStatus = "active",
                ActiveModules = new List<string> { "sales", "financials", "purchases", "reports" }
            });
        }

        #region Phase 1: Chart of Accounts Tests

        [Fact]
        public async Task GetChartOfAccounts_ThrowsWhenTenantNotResolved()
        {
            _mockTenant.Setup(t => t.IsResolved).Returns(false);
            _mockTenant.Setup(t => t.CompanyId).Returns((int?)null);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tools.GetChartOfAccounts());

            Assert.Contains("Tenant context is required", ex.Message);
        }

        [Fact]
        public async Task GetChartOfAccounts_ReturnsPlanRestrictedWhenSubscriptionModuleDisabled()
        {
            _mockAdmin.Setup(a => a.IsModuleEnabled("financials")).Returns(false);
            _mockAdmin.Setup(a => a.IsModuleEnabled("reports")).Returns(false);
            _mockAdmin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto
            {
                CompanyId = 1,
                Slug = "ph-financials",
                Plan = "invoicing",
                ActiveModules = new List<string> { "sales", "purchases" }
            });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetChartOfAccounts();

            Assert.Equal(FinancialMcpStatus.PlanRestricted, response.Status);
            Assert.Contains("Invoicing", response.Message);
            Assert.Contains("Settings > Subscription", response.Message);
            Assert.Equal("accounting", response.UpgradePlanHint);
            Assert.Equal(new[] { "financials", "reports" }, response.RequiredModules);
            Assert.Empty(response.Accounts);
            Assert.Equal(0, response.TotalAccounts);
            _mockFinancial.Verify(s => s.GetAccounts(), Times.Never);
        }

        [Fact]
        public async Task GetChartOfAccounts_ReturnsPlanRestrictedWhenAccountingPlanModulesDisabled()
        {
            _mockAdmin.Setup(a => a.IsModuleEnabled("financials")).Returns(false);
            _mockAdmin.Setup(a => a.IsModuleEnabled("reports")).Returns(false);
            _mockAdmin.Setup(a => a.GetCurrentSubscription()).Returns(new TenantSubscriptionDto
            {
                CompanyId = 1,
                Slug = "ph-financials",
                Plan = "accounting",
                ActiveModules = new List<string> { "sales", "purchases" }
            });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetChartOfAccounts();

            Assert.Equal(FinancialMcpStatus.PlanRestricted, response.Status);
            Assert.Contains("Accounting", response.Message);
            Assert.Contains("financials", response.Message);
            Assert.Null(response.UpgradePlanHint);
            Assert.Empty(response.Accounts);
            _mockFinancial.Verify(s => s.GetAccounts(), Times.Never);
        }

        [Fact]
        public async Task GetChartOfAccounts_ReturnsAccountsAndSummarySuccessfully()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };
            var liabilityClass = new AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" };

            var accounts = new List<Core.Domain.Financials.Account>
            {
                new Core.Domain.Financials.Account
                {
                    Id = 1,
                    AccountCode = "1010",
                    AccountName = "Cash on Hand",
                    AccountClass = assetClass,
                    DrOrCrSide = DrOrCrSide.Dr,
                    IsCash = true
                },
                new Core.Domain.Financials.Account
                {
                    Id = 2,
                    AccountCode = "1090",
                    AccountName = "Allowance for Doubtful Accounts",
                    AccountClass = assetClass,
                    DrOrCrSide = DrOrCrSide.Cr, // Contra Asset has Cr normal balance
                    IsContraAccount = true
                },
                new Core.Domain.Financials.Account
                {
                    Id = 3,
                    AccountCode = "2010",
                    AccountName = "Accounts Payable",
                    AccountClass = liabilityClass,
                    DrOrCrSide = DrOrCrSide.Cr
                }
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var result = await tools.GetChartOfAccounts();

            Assert.NotNull(result);
            Assert.Equal(FinancialMcpStatus.Success, result.Status);
            Assert.Null(result.Message);
            Assert.Equal("ph-financials", result.Organization);
            Assert.Equal(3, result.TotalAccounts);
            Assert.Equal(3, result.TotalUnfilteredAccounts);
            Assert.Equal(2, result.ClassificationCounts["Assets"]);
            Assert.Equal(1, result.ClassificationCounts["Liabilities"]);

            // Normal asset account
            var cashAccount = result.Accounts.First(a => a.Code == "1010");
            Assert.Equal("Dr", cashAccount.NormalBalance);
            Assert.False(cashAccount.IsContraAccount);

            // Contra asset account must report Cr normal balance
            var contraAccount = result.Accounts.First(a => a.Code == "1090");
            Assert.Equal("Cr", contraAccount.NormalBalance);
            Assert.True(contraAccount.IsContraAccount);
        }

        [Fact]
        public async Task GetChartOfAccounts_FiltersByCodeOrName()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };

            var accounts = new List<Core.Domain.Financials.Account>
            {
                new Core.Domain.Financials.Account { Id = 1, AccountCode = "1010", AccountName = "Cash on Hand", AccountClass = assetClass },
                new Core.Domain.Financials.Account { Id = 2, AccountCode = "1020", AccountName = "Petty Cash", AccountClass = assetClass },
                new Core.Domain.Financials.Account { Id = 3, AccountCode = "1050", AccountName = "Accounts Receivable", AccountClass = assetClass }
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var filtered = await tools.GetChartOfAccounts(filter: "Petty");

            Assert.Single(filtered.Accounts);
            Assert.Equal(1, filtered.TotalAccounts);
            Assert.Equal(3, filtered.TotalUnfilteredAccounts);
            Assert.Equal("1020", filtered.Accounts[0].Code);
            Assert.Equal("Petty Cash", filtered.Accounts[0].Name);
            Assert.Equal(1, filtered.ClassificationCounts["Assets"]);
        }

        [Fact]
        public async Task GetChartOfAccounts_FiltersByAccountClass()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };
            var expenseClass = new AccountClass { Id = 5, Name = "Expenses", NormalBalance = "Dr" };

            var accounts = new List<Core.Domain.Financials.Account>
            {
                new Core.Domain.Financials.Account { Id = 1, AccountCode = "1010", AccountName = "Cash", AccountClass = assetClass },
                new Core.Domain.Financials.Account { Id = 2, AccountCode = "5010", AccountName = "Rent Expense", AccountClass = expenseClass }
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var filtered = await tools.GetChartOfAccounts(accountClass: "Expense");

            Assert.Single(filtered.Accounts);
            Assert.Equal("5010", filtered.Accounts[0].Code);
            Assert.Equal("Expenses", filtered.Accounts[0].AccountClass);
            Assert.Equal(1, filtered.ClassificationCounts["Expenses"]);
            Assert.False(filtered.ClassificationCounts.ContainsKey("Assets"));
        }

        [Fact]
        public async Task GetChartOfAccounts_FiltersOutZeroBalances_WhenIncludeZeroBalancesFalse()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };

            var accountWithBalance = new Core.Domain.Financials.Account
            {
                Id = 1,
                AccountCode = "1010",
                AccountName = "Operating Account",
                AccountClass = assetClass,
                DrOrCrSide = DrOrCrSide.Dr,
                GeneralLedgerLines = new List<GeneralLedgerLine>
                {
                    new GeneralLedgerLine { Amount = 500m, DrCr = DrOrCrSide.Dr }
                }
            };

            var accountZeroBalance = new Core.Domain.Financials.Account
            {
                Id = 2,
                AccountCode = "1020",
                AccountName = "Dormant Account",
                AccountClass = assetClass,
                DrOrCrSide = DrOrCrSide.Dr,
                GeneralLedgerLines = new List<GeneralLedgerLine>()
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(new List<Core.Domain.Financials.Account> { accountWithBalance, accountZeroBalance });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var result = await tools.GetChartOfAccounts(includeZeroBalances: false);

            Assert.Single(result.Accounts);
            Assert.Equal("1010", result.Accounts[0].Code);
            Assert.Equal(500m, result.Accounts[0].Balance);
            Assert.Equal(1, result.TotalAccounts);
            Assert.Equal(2, result.TotalUnfilteredAccounts);
        }

        [Fact]
        public async Task GetChartOfAccounts_HandlesNullAccountClassSafely()
        {
            var accountWithoutClass = new Core.Domain.Financials.Account
            {
                Id = 1,
                AccountCode = "9999",
                AccountName = "Unclassified Suspense",
                AccountClass = null,
                DrOrCrSide = DrOrCrSide.Dr,
                GeneralLedgerLines = new List<GeneralLedgerLine>
                {
                    new GeneralLedgerLine { Amount = 100m, DrCr = DrOrCrSide.Dr }
                }
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(new List<Core.Domain.Financials.Account> { accountWithoutClass });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var result = await tools.GetChartOfAccounts(includeZeroBalances: false);

            Assert.Single(result.Accounts);
            Assert.Equal("Unknown", result.Accounts[0].AccountClass);
            Assert.Equal("Dr", result.Accounts[0].NormalBalance);
            Assert.Equal(100m, result.Accounts[0].Balance);
        }

        #endregion

        #region Phase 2: Financial Reports & Ledger Drilldown Tests

        [Fact]
        public async Task GetTrialBalance_ReturnsBalancedReport()
        {
            var tbItems = new List<Services.Financial.TrialBalance>
            {
                new Services.Financial.TrialBalance { AccountId = 1, AccountCode = "1010", AccountName = "Cash", Debit = 1000m, Credit = 0m },
                new Services.Financial.TrialBalance { AccountId = 2, AccountCode = "2010", AccountName = "Accounts Payable", Debit = 0m, Credit = 1000m }
            };

            _mockFinancial.Setup(s => s.TrialBalance(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Returns(tbItems);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetTrialBalance(fromDate: "2026-01-01", toDate: "2026-12-31", accountingBasis: "Accrual");

            Assert.NotNull(response);
            Assert.Equal("ph-financials", response.Organization);
            Assert.Equal(1000m, response.TotalDebit);
            Assert.Equal(1000m, response.TotalCredit);
            Assert.True(response.IsBalanced);
            Assert.Equal(0m, response.Difference);
            Assert.Equal(2, response.AccountCount);
        }

        [Fact]
        public async Task GetTrialBalance_ReturnsAccessDeniedWhenPermissionDenied()
        {
            _mockTenant.Setup(t => t.Role).Returns(CompanyMemberRole.Member);
            _mockTenant.Setup(t => t.Permissions).Returns(new HashSet<string>());

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetTrialBalance();

            Assert.Equal(FinancialMcpStatus.AccessDenied, response.Status);
            Assert.Contains("Access denied", response.Message);
            Assert.Contains(SystemPermissions.ReportsView, response.Message);
            Assert.Empty(response.Items);
            _mockFinancial.Verify(
                s => s.TrialBalance(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AccountingBasis>()),
                Times.Never);
        }

        [Fact]
        public async Task GetTrialBalance_OwnerBypassesPermissionCheck()
        {
            _mockTenant.Setup(t => t.Role).Returns(CompanyMemberRole.Owner);
            _mockTenant.Setup(t => t.Permissions).Returns(new HashSet<string>());
            _mockFinancial.Setup(s => s.TrialBalance(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), AccountingBasis.Accrual))
                .Returns(new List<Services.Financial.TrialBalance>());

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetTrialBalance();

            Assert.Equal(FinancialMcpStatus.Success, response.Status);
            Assert.Empty(response.Items);
        }

        [Fact]
        public async Task GetBalanceSheet_CalculatesTotalsAndNetAssetsCorrectly()
        {
            var bsItems = new List<Services.Financial.BalanceSheet>
            {
                new Services.Financial.BalanceSheet { AccountId = 1, AccountClassId = 1, AccountCode = "1010", AccountName = "Cash", Amount = 5000m },
                new Services.Financial.BalanceSheet { AccountId = 2, AccountClassId = 2, AccountCode = "2010", AccountName = "Payables", Amount = 2000m },
                new Services.Financial.BalanceSheet { AccountId = 3, AccountClassId = 3, AccountCode = "3010", AccountName = "Retained Earnings", Amount = 3000m }
            };

            _mockFinancial.Setup(s => s.BalanceSheet(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Returns(bsItems);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetBalanceSheet(asOfDate: "2026-12-31");

            Assert.NotNull(response);
            Assert.Equal(5000m, response.TotalAssets);
            Assert.Equal(2000m, response.TotalLiabilities);
            Assert.Equal(3000m, response.TotalEquity);
            Assert.Equal(3000m, response.NetAssets); // Assets - Liabilities
            Assert.True(response.IsBalanced);
            Assert.Equal(3, response.Items.Count);
        }

        [Fact]
        public async Task GetIncomeStatement_CalculatesRevenuesExpensesAndNetIncome()
        {
            var incomeItems = new List<Services.Financial.IncomeStatement>
            {
                new Services.Financial.IncomeStatement { AccountId = 1, IsExpense = false, AccountCode = "4010", AccountName = "Consulting Revenue", Amount = 10000m },
                new Services.Financial.IncomeStatement { AccountId = 2, IsExpense = true, AccountCode = "5010", AccountName = "Rent Expense", Amount = 3000m },
                new Services.Financial.IncomeStatement { AccountId = 3, IsExpense = true, AccountCode = "5020", AccountName = "Utilities", Amount = 1000m }
            };

            _mockFinancial.Setup(s => s.IncomeStatement(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Returns(incomeItems);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetIncomeStatement(fromDate: "2026-01-01", toDate: "2026-06-30");

            Assert.NotNull(response);
            Assert.Equal(10000m, response.TotalRevenue);
            Assert.Equal(4000m, response.TotalExpense);
            Assert.Equal(6000m, response.NetIncome);
            Assert.Equal(3, response.Items.Count);
        }

        [Fact]
        public async Task GetGeneralLedgerTransactions_ValidatesPaginationAndReturnsPagedResults()
        {
            var page = new GeneralLedgerTransactionPage
            {
                TotalCount = 100,
                Skip = 10,
                Take = 20,
                Items = new List<GeneralLedgerTransaction>
                {
                    new GeneralLedgerTransaction
                    {
                        Id = 1,
                        Date = DateTime.UtcNow,
                        DocumentType = "SalesInvoice",
                        DocumentTypeLabel = "Sales Invoice",
                        Description = "Test invoice journal",
                        TotalDebit = 500m,
                        TotalCredit = 500m
                    }
                }
            };

            _mockFinancial.Setup(s => s.GetGeneralLedgerTransactions(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DocumentTypes?>(),
                10, 20))
                .Returns(page);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetGeneralLedgerTransactions(skip: 10, take: 20);

            Assert.NotNull(response);
            Assert.Equal(100, response.TotalCount);
            Assert.Equal(10, response.Skip);
            Assert.Equal(20, response.Take);
            Assert.Single(response.Transactions);
            Assert.Equal("SalesInvoice", response.Transactions[0].DocumentType);
        }

        [Fact]
        public async Task DateValidation_ThrowsWhenStartAfterEnd()
        {
            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                tools.GetTrialBalance(fromDate: "2026-12-31", toDate: "2026-01-01"));

            Assert.Contains("Start date must not be after end date", ex.Message);
        }

        [Fact]
        public async Task GetGeneralLedgerTransactions_ThrowsOnInvalidTake()
        {
            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                tools.GetGeneralLedgerTransactions(take: 501));

            Assert.Contains("Take must be between 1 and 500", ex.Message);
        }

        [Fact]
        public async Task GetGeneralLedgerTransactions_ReturnsAccessDeniedWhenMemberOnlyHasReportsViewPermission()
        {
            _mockTenant.Setup(t => t.Role).Returns(CompanyMemberRole.Member);
            _mockTenant.Setup(t => t.Permissions).Returns(new HashSet<string> { SystemPermissions.ReportsView });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var response = await tools.GetGeneralLedgerTransactions();

            Assert.Equal(FinancialMcpStatus.AccessDenied, response.Status);
            Assert.Contains(SystemPermissions.FinancialsJournalView, response.Message);
            Assert.Empty(response.Transactions);
            _mockFinancial.Verify(
                s => s.GetGeneralLedgerTransactions(
                    It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                    It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<DocumentTypes?>(),
                    It.IsAny<int>(), It.IsAny<int>()),
                Times.Never);
        }

        [Fact]
        public async Task GetTrialBalance_ExpandsDateOnlyEndDateToEndOfDay()
        {
            DateTime? capturedFrom = null;
            DateTime? capturedTo = null;

            _mockFinancial.Setup(s => s.TrialBalance(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Callback<DateTime?, DateTime?, Core.Domain.AccountingBasis>((f, t, _) =>
                {
                    capturedFrom = f;
                    capturedTo = t;
                })
                .Returns(new List<Services.Financial.TrialBalance>());

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            await tools.GetTrialBalance(fromDate: "2026-01-01", toDate: "2026-01-01");

            Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0), capturedFrom);
            Assert.Equal(new DateTime(2026, 1, 1).AddDays(1).AddTicks(-1), capturedTo);
        }

        [Fact]
        public async Task GetTrialBalance_ThrowsOnUndefinedNumericAccountingBasis()
        {
            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                tools.GetTrialBalance(accountingBasis: "999"));

            Assert.Contains("Invalid accounting basis", ex.Message);
        }

        [Theory]
        [InlineData("999")]
        [InlineData("Manual")]
        public async Task GetGeneralLedgerTransactions_ThrowsOnInvalidOrNumericDocumentType_WithHelpfulMessage(string invalidDocType)
        {
            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                tools.GetGeneralLedgerTransactions(documentType: invalidDocType));

            Assert.Contains("Invalid document type", ex.Message);
            Assert.Contains(nameof(DocumentTypes.JournalEntry), ex.Message);
        }

        [Fact]
        public async Task GetBalanceSheet_And_GetIncomeStatement_FilterZeroBalances_WhenIncludeZeroBalancesFalse()
        {
            var bsItems = new List<Services.Financial.BalanceSheet>
            {
                new Services.Financial.BalanceSheet { AccountId = 1, AccountClassId = 1, AccountCode = "1010", AccountName = "Cash", Amount = 5000m },
                new Services.Financial.BalanceSheet { AccountId = 2, AccountClassId = 1, AccountCode = "1020", AccountName = "Petty Cash", Amount = 0m },
                new Services.Financial.BalanceSheet { AccountId = 3, AccountClassId = 3, AccountCode = "3010", AccountName = "Equity", Amount = 5000m }
            };

            var incomeItems = new List<Services.Financial.IncomeStatement>
            {
                new Services.Financial.IncomeStatement { AccountId = 4, IsExpense = false, AccountCode = "4010", AccountName = "Sales", Amount = 2500m },
                new Services.Financial.IncomeStatement { AccountId = 5, IsExpense = true, AccountCode = "5010", AccountName = "Unused Expense", Amount = 0m }
            };

            _mockFinancial.Setup(s => s.BalanceSheet(null, It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Returns(bsItems);
            _mockFinancial.Setup(s => s.IncomeStatement(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), Core.Domain.AccountingBasis.Accrual))
                .Returns(incomeItems);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var bsResponse = await tools.GetBalanceSheet(asOfDate: "2026-12-31", includeZeroBalances: false);
            var isResponse = await tools.GetIncomeStatement(fromDate: "2026-01-01", toDate: "2026-12-31", includeZeroBalances: false);

            Assert.Equal(2, bsResponse.Items.Count);
            Assert.DoesNotContain(bsResponse.Items, i => i.AccountCode == "1020");

            Assert.Single(isResponse.Items);
            Assert.Equal("4010", isResponse.Items[0].AccountCode);
        }

        #endregion
    }
}

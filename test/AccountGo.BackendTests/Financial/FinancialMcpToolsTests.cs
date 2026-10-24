#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Mcp.Tools;
using Core.Domain;
using Core.Domain.Financials;
using Core.Tenancy;
using Moq;
using Services.Administration;
using Services.Financial;
using Xunit;

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

            _mockAdmin.Setup(a => a.IsModuleEnabled("financials")).Returns(true);
            _mockAdmin.Setup(a => a.IsModuleEnabled("reports")).Returns(true);
        }

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
        public async Task GetChartOfAccounts_ThrowsWhenSubscriptionModuleDisabled()
        {
            _mockAdmin.Setup(a => a.IsModuleEnabled("financials")).Returns(false);
            _mockAdmin.Setup(a => a.IsModuleEnabled("reports")).Returns(false);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tools.GetChartOfAccounts());

            Assert.Contains("subscription plan does not include", ex.Message);
        }

        [Fact]
        public async Task GetChartOfAccounts_ReturnsAccountsAndSummarySuccessfully()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };
            var liabilityClass = new AccountClass { Id = 2, Name = "Liabilities", NormalBalance = "Cr" };

            var accounts = new List<Account>
            {
                new Account
                {
                    Id = 1,
                    AccountCode = "1010",
                    AccountName = "Cash on Hand",
                    AccountClass = assetClass,
                    DrOrCrSide = DrOrCrSide.Dr,
                    IsCash = true
                },
                new Account
                {
                    Id = 2,
                    AccountCode = "1090",
                    AccountName = "Allowance for Doubtful Accounts",
                    AccountClass = assetClass,
                    DrOrCrSide = DrOrCrSide.Cr, // Contra Asset has Cr normal balance
                    IsContraAccount = true
                },
                new Account
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

            var accounts = new List<Account>
            {
                new Account { Id = 1, AccountCode = "1010", AccountName = "Cash on Hand", AccountClass = assetClass },
                new Account { Id = 2, AccountCode = "1020", AccountName = "Petty Cash", AccountClass = assetClass },
                new Account { Id = 3, AccountCode = "1050", AccountName = "Accounts Receivable", AccountClass = assetClass }
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var filtered = await tools.GetChartOfAccounts(filter: "Petty");

            Assert.Single(filtered.Accounts);
            Assert.Equal(1, filtered.TotalAccounts);
            Assert.Equal(3, filtered.TotalUnfilteredAccounts);
            Assert.Equal("1020", filtered.Accounts[0].Code);
            Assert.Equal("Petty Cash", filtered.Accounts[0].Name);
            // Summary count matches filtered count
            Assert.Equal(1, filtered.ClassificationCounts["Assets"]);
        }

        [Fact]
        public async Task GetChartOfAccounts_FiltersByAccountClass()
        {
            var assetClass = new AccountClass { Id = 1, Name = "Assets", NormalBalance = "Dr" };
            var expenseClass = new AccountClass { Id = 5, Name = "Expenses", NormalBalance = "Dr" };

            var accounts = new List<Account>
            {
                new Account { Id = 1, AccountCode = "1010", AccountName = "Cash", AccountClass = assetClass },
                new Account { Id = 2, AccountCode = "5010", AccountName = "Rent Expense", AccountClass = expenseClass }
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

            // Account 1 has a GL line (non-zero balance), Account 2 has no GL line (zero balance)
            var accountWithBalance = new Account
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

            var accountZeroBalance = new Account
            {
                Id = 2,
                AccountCode = "1020",
                AccountName = "Dormant Account",
                AccountClass = assetClass,
                DrOrCrSide = DrOrCrSide.Dr,
                GeneralLedgerLines = new List<GeneralLedgerLine>()
            };

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(new List<Account> { accountWithBalance, accountZeroBalance });

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
            var accountWithoutClass = new Account
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

            _mockFinancial.Setup(s => s.GetAccounts()).Returns(new List<Account> { accountWithoutClass });

            var tools = new FinancialMcpTools(_mockFinancial.Object, _mockAdmin.Object, _mockTenant.Object);

            var result = await tools.GetChartOfAccounts(includeZeroBalances: false);

            Assert.Single(result.Accounts);
            Assert.Equal("Unknown", result.Accounts[0].AccountClass);
            Assert.Equal("Dr", result.Accounts[0].NormalBalance);
            Assert.Equal(100m, result.Accounts[0].Balance);
        }
    }
}

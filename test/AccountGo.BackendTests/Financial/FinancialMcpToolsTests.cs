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
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Financial
{
    public class FinancialMcpToolsTests
    {
        [Fact]
        public async Task GetChartOfAccounts_ThrowsWhenTenantNotResolved()
        {
            var mockFinancial = new Mock<IFinancialService>();
            var mockTenant = new Mock<ITenantContext>();
            mockTenant.Setup(t => t.IsResolved).Returns(false);
            mockTenant.Setup(t => t.CompanyId).Returns((int?)null);

            var tools = new FinancialMcpTools(mockFinancial.Object, mockTenant.Object);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tools.GetChartOfAccounts());

            Assert.Contains("Tenant context is required", ex.Message);
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
                    AccountCode = "2010",
                    AccountName = "Accounts Payable",
                    AccountClass = liabilityClass,
                    DrOrCrSide = DrOrCrSide.Cr
                }
            };

            var mockFinancial = new Mock<IFinancialService>();
            mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var mockTenant = new Mock<ITenantContext>();
            mockTenant.Setup(t => t.IsResolved).Returns(true);
            mockTenant.Setup(t => t.CompanyId).Returns(1);
            mockTenant.Setup(t => t.CompanySlug).Returns("ph-financials");

            var tools = new FinancialMcpTools(mockFinancial.Object, mockTenant.Object);

            var result = await tools.GetChartOfAccounts();

            Assert.NotNull(result);
            Assert.Equal("ph-financials", result.Organization);
            Assert.Equal(2, result.TotalAccounts);
            Assert.Equal(1, result.ClassificationCounts["Assets"]);
            Assert.Equal(1, result.ClassificationCounts["Liabilities"]);

            var cashAccount = result.Accounts.First(a => a.Code == "1010");
            Assert.Equal("Cash on Hand", cashAccount.Name);
            Assert.Equal("Assets", cashAccount.AccountClass);
            Assert.Equal("Dr", cashAccount.NormalBalance);
            Assert.True(cashAccount.IsCash);
            Assert.True(cashAccount.CanPost);
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

            var mockFinancial = new Mock<IFinancialService>();
            mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var mockTenant = new Mock<ITenantContext>();
            mockTenant.Setup(t => t.IsResolved).Returns(true);
            mockTenant.Setup(t => t.CompanyId).Returns(1);
            mockTenant.Setup(t => t.CompanySlug).Returns("test-org");

            var tools = new FinancialMcpTools(mockFinancial.Object, mockTenant.Object);

            var filtered = await tools.GetChartOfAccounts(filter: "Petty");

            Assert.Single(filtered.Accounts);
            Assert.Equal("1020", filtered.Accounts[0].Code);
            Assert.Equal("Petty Cash", filtered.Accounts[0].Name);
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

            var mockFinancial = new Mock<IFinancialService>();
            mockFinancial.Setup(s => s.GetAccounts()).Returns(accounts);

            var mockTenant = new Mock<ITenantContext>();
            mockTenant.Setup(t => t.IsResolved).Returns(true);
            mockTenant.Setup(t => t.CompanyId).Returns(1);

            var tools = new FinancialMcpTools(mockFinancial.Object, mockTenant.Object);

            var filtered = await tools.GetChartOfAccounts(accountClass: "Expense");

            Assert.Single(filtered.Accounts);
            Assert.Equal("5010", filtered.Accounts[0].Code);
            Assert.Equal("Expenses", filtered.Accounts[0].AccountClass);
        }
    }
}

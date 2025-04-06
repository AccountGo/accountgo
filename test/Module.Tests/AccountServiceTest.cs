using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Core.Domain.Financials;
using Api.Data;
using Core.Domain;

namespace Module.Tests
{
    public class AccountServiceTests
    {
        private async Task<ApiDbContext> GetInMemoryDbContextAsync()
        {
            var options = new DbContextOptionsBuilder<ApiDbContext>()
                .UseInMemoryDatabase(databaseName: $"TestDb_{System.Guid.NewGuid()}")
                .Options;

            var context = new ApiDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return context;
        }

        private Account GetTestAccount(string code = "TEST123") => new Account
        {
            AccountCode = code,
            AccountName = "Test Account",
            AccountClassId = 1,
            CompanyId = 1,
            DrOrCrSide = DrOrCrSide.Dr,
            Description = "Test Description"
        };

        [Fact]
        public async Task AddAccountAsync_ShouldAddAccount()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var testAccount = GetTestAccount();

            var result = await service.AddAccountAsync(testAccount);

            Assert.NotNull(result);
            Assert.Single(context.Accounts);
            Assert.Equal(testAccount.AccountCode, result.AccountCode);
        }

        [Fact]
        public async Task UpdateAccountAsync_ShouldUpdateAccountName()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var account = GetTestAccount();
            await context.Accounts.AddAsync(account, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            account.AccountName = "Updated Name";
            var updated = await service.UpdateAccountAsync(account.AccountCode, account);

            Assert.NotNull(updated);
            Assert.Equal("Updated Name", updated.AccountName);
        }

        [Fact]
        public async Task UpdateAccountAsync_ShouldReturnNull_WhenNotFound()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var result = await service.UpdateAccountAsync("NON_EXISTENT", GetTestAccount());

            Assert.Null(result);
        }

        [Fact]
        public async Task DeleteAccountAsync_ShouldRemoveAccount()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var account = GetTestAccount();
            await context.Accounts.AddAsync(account, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var deleted = await service.DeleteAccountAsync(account.AccountCode);

            Assert.NotNull(deleted);
            Assert.Empty(context.Accounts);
        }

        [Fact]
        public async Task DeleteAccountAsync_ShouldReturnNull_WhenNotFound()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var deleted = await service.DeleteAccountAsync("NON_EXISTENT");

            Assert.Null(deleted);
        }

        [Fact]
        public async Task GetAccountByCodeAsync_ShouldReturnAccount()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);
            var account = GetTestAccount();
            await context.Accounts.AddAsync(account, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await service.GetAccountByCodeAsync(account.AccountCode);

            Assert.NotNull(result);
            Assert.Equal(account.AccountCode, result?.AccountCode);
        }

        [Fact]
        public async Task GetAllAccountsAsync_ShouldReturnAllAccounts()
        {
            var context = await GetInMemoryDbContextAsync();
            var service = new AccountService(context);

            await context.Accounts.AddRangeAsync(new List<Account>
            {
                GetTestAccount("ACC1"),
                GetTestAccount("ACC2"),
            }, TestContext.Current.CancellationToken);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            var result = await service.GetAllAccountsAsync();

            Assert.Equal(2, result.Count());
        }
    }
}

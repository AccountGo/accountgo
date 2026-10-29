// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Core.Entitlements;
using Core.Tenancy;
using Dto.Banking;
using Moq;
using Services.Banking;
using Xunit;

namespace AccountGo.BackendTests.Banking;

public class BankAccountServiceTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("12", "•••• 12")]
    [InlineData("1234", "•••• 1234")]
    [InlineData("1234567890", "•••• 7890")]
    [InlineData("BDO-0012-3456", "•••• 3456")]
    public void MaskAccountNumber_MasksToLastFour(string? input, string? expected)
    {
        Assert.Equal(expected, BankAccountService.MaskAccountNumber(input));
    }

    [Fact]
    public void EnsureEligiblePostingAccount_RejectsAccountsReceivable10120()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var ar = harness.AddAccount("10120", "Accounts Receivable", parentCode: "10100", isCash: false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Service.EnsureEligiblePostingAccount(ar.Id, exceptBankId: null));
        Assert.Equal("Pick a postable account.", ex.Message);
    }

    [Fact]
    public void EnsureEligiblePostingAccount_RejectsAccountAlreadyUsedByAnotherBank()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var checking = harness.RequireAccount("10111");
        harness.AddBank("Existing", checking.Id);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            harness.Service.EnsureEligiblePostingAccount(checking.Id, exceptBankId: null));
        Assert.Equal("That account already belongs to Existing.", ex.Message);
    }

    [Fact]
    public void Create_FirstBank_AdoptsLowestPostableCashAccount()
    {
        using var harness = new Harness();
        harness.SeedCashTree();

        var created = harness.Service.Create(new CreateBankAccountRequest
        {
            Name = "Operating",
            Type = (int)BankTypes.CheckingAccount
        }, includeFullNumbers: true);

        Assert.Equal("Operating", created.Name);
        Assert.Equal(harness.RequireAccount("10111").Id, created.AccountId);
        Assert.True(created.IsDefaultForReceipts);
        Assert.True(created.IsDefaultForPayments);
        Assert.Equal("Operating", harness.RequireAccount("10111").AccountName);
    }

    [Fact]
    public void Create_SecondBank_AllocatesPreferredCode()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.Service.Create(new CreateBankAccountRequest { Name = "First", Type = 1 }, includeFullNumbers: false);

        var second = harness.Service.Create(new CreateBankAccountRequest { Name = "Second", Type = 1 }, includeFullNumbers: false);

        var account = harness.Accounts.GetById(second.AccountId!.Value)!;
        Assert.Equal("10114", account.AccountCode);
        Assert.Equal("Second", account.AccountName);
    }

    [Fact]
    public void Create_WhenPreferredCodesFull_UsesNextFreeNonReserved()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        for (var code = 10114; code <= 10119; code++)
            harness.AddAccount(code.ToString(), $"Slot {code}", parentCode: "10110", isCash: false);

        harness.Service.Create(new CreateBankAccountRequest { Name = "First", Type = 1 }, includeFullNumbers: false);
        var second = harness.Service.Create(new CreateBankAccountRequest { Name = "Overflow", Type = 1 }, includeFullNumbers: false);

        var account = harness.Accounts.GetById(second.AccountId!.Value)!;
        Assert.Equal("10101", account.AccountCode);
    }

    [Fact]
    public void Archive_ClearsDefaults_AndUnarchiveRestoresSlot()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var first = harness.Service.Create(new CreateBankAccountRequest { Name = "A", Type = 1 }, includeFullNumbers: false);
        var second = harness.Service.Create(new CreateBankAccountRequest { Name = "B", Type = 1 }, includeFullNumbers: false);

        harness.Service.Archive(first.Id, includeFullNumbers: false);
        var archived = harness.Service.Get(first.Id, includeFullNumbers: false);
        Assert.NotNull(archived.ArchivedAt);
        Assert.False(archived.IsDefaultForReceipts);

        var restored = harness.Service.Unarchive(first.Id, includeFullNumbers: false);
        Assert.Null(restored.ArchivedAt);
        Assert.Equal(second.Id, harness.Service.Get(second.Id, includeFullNumbers: false).Id);
    }

    [Fact]
    public void List_MasksNumbersUnlessManage()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        harness.Service.Create(new CreateBankAccountRequest
        {
            Name = "Masked",
            Type = 1,
            AccountNumber = "1234567890"
        }, includeFullNumbers: true);

        var view = harness.Service.List(includeArchived: false, includeFullNumbers: false).Accounts.Single();
        Assert.Equal("•••• 7890", view.AccountNumberMasked);
        Assert.Null(view.AccountNumber);

        var manage = harness.Service.List(includeArchived: false, includeFullNumbers: true).Accounts.Single();
        Assert.Equal("1234567890", manage.AccountNumber);
    }

    [Fact]
    public void DeleteOrArchive_HardDeletesBankWithNoHistory()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var created = harness.Service.Create(new CreateBankAccountRequest { Name = "Temp", Type = 1 }, includeFullNumbers: false);
        // First bank adopted 10111; create a second dedicated account then delete it.
        var dedicated = harness.Service.Create(new CreateBankAccountRequest { Name = "Disposable", Type = 1 }, includeFullNumbers: false);
        var accountId = dedicated.AccountId!.Value;

        harness.Service.DeleteOrArchive(dedicated.Id);

        Assert.Null(harness.Banks.GetById(dedicated.Id));
        Assert.Null(harness.Accounts.GetById(accountId));
        Assert.NotNull(harness.Banks.GetById(created.Id));
    }

    [Fact]
    public void SetDefault_ClearsOtherBanksDefaultFlag()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var first = harness.Service.Create(new CreateBankAccountRequest { Name = "A", Type = 1 }, includeFullNumbers: false);
        var second = harness.Service.Create(new CreateBankAccountRequest { Name = "B", Type = 1 }, includeFullNumbers: false);

        harness.Service.SetDefault(second.Id, "receipts", includeFullNumbers: false);

        Assert.False(harness.Service.Get(first.Id, includeFullNumbers: false).IsDefaultForReceipts);
        Assert.True(harness.Service.Get(second.Id, includeFullNumbers: false).IsDefaultForReceipts);
    }

    [Fact]
    public void SyncBankNameFromAccount_UpdatesBankAndRejectsClash()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var first = harness.Service.Create(new CreateBankAccountRequest { Name = "Alpha", Type = 1 }, includeFullNumbers: false);
        harness.Service.Create(new CreateBankAccountRequest { Name = "Beta", Type = 1 }, includeFullNumbers: false);

        var account = harness.Accounts.GetById(first.AccountId!.Value)!;
        account.AccountName = "Alpha Renamed";
        harness.Service.SyncBankNameFromAccount(account);
        Assert.Equal("Alpha Renamed", harness.Service.Get(first.Id, includeFullNumbers: false).Name);

        account.AccountName = "Beta";
        var ex = Assert.Throws<InvalidOperationException>(() => harness.Service.SyncBankNameFromAccount(account));
        Assert.Contains("already exists", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureOwned_GetRejectsCrossTenantBankId()
    {
        using var harness = new Harness();
        harness.SeedCashTree();
        var created = harness.Service.Create(new CreateBankAccountRequest { Name = "Local", Type = 1 }, includeFullNumbers: false);
        var bank = harness.Banks.GetById(created.Id)!;
        bank.CompanyId = 99;

        Assert.Throws<TenantEntityNotFoundException>(() =>
            harness.Service.Get(created.Id, includeFullNumbers: false));
    }

    private sealed class Harness : IDisposable
    {
        private readonly InMemoryRepos _repos;
        public BankAccountService Service { get; }
        public IRepository<Account> Accounts => _repos.Accounts;
        public IRepository<Bank> Banks => _repos.Banks;

        public Harness()
        {
            _repos = new InMemoryRepos();
            var tenant = new FakeTenant(companyId: 1);
            var entitlements = new Mock<IEntitlementService>();
            entitlements.Setup(e => e.EnsureCanCreate(It.IsAny<MeteredResource>(), It.IsAny<int>()));
            Service = new BankAccountService(
                _repos.Banks,
                _repos.Accounts,
                _repos.GlLines,
                _repos.GlHeaders,
                _repos.Settings,
                entitlements.Object,
                tenant);
            _repos.Settings.Insert(new CompanySetting { CompanyId = 1, CoaTemplateId = CoaTemplates.Legacy });
        }

        public void SeedCashTree()
        {
            AddAccount("10100", "Current Assets", parentCode: null, isCash: false, id: 1);
            AddAccount("10110", "Cash in Hand and in Bank", parentCode: "10100", isCash: true, id: 2);
            AddAccount("10111", "Regular Checking Account", parentCode: "10110", isCash: false, id: 3);
            AddAccount("10112", "Savings", parentCode: "10110", isCash: false, id: 4);
            AddAccount("10113", "Cash in Hand", parentCode: "10110", isCash: false, id: 5);
        }

        public Account AddAccount(string code, string name, string? parentCode, bool isCash, int? id = null)
        {
            int? parentId = null;
            if (parentCode != null)
                parentId = RequireAccount(parentCode).Id;

            var account = new Account
            {
                Id = id ?? _repos.NextAccountId(),
                CompanyId = 1,
                AccountCode = code,
                AccountName = name,
                ParentAccountId = parentId,
                AccountClassId = (int)AccountClasses.Assets,
                DrOrCrSide = DrOrCrSide.Dr,
                IsCash = isCash
            };
            _repos.Accounts.Insert(account);
            return account;
        }

        public Bank AddBank(string name, int accountId)
        {
            var bank = new Bank
            {
                Id = _repos.NextBankId(),
                CompanyId = 1,
                Name = name,
                AccountId = accountId,
                Type = BankTypes.CheckingAccount,
                IsActive = true
            };
            _repos.Banks.Insert(bank);
            return bank;
        }

        public Account RequireAccount(string code) =>
            _repos.Accounts.Table.Single(a => a.AccountCode == code);

        public void Dispose() { }
    }

    private sealed class FakeTenant : ITenantContext
    {
        public FakeTenant(int companyId) => CompanyId = companyId;
        public int? CompanyId { get; }
        public string? CompanySlug => "test";
        public CompanyMemberRole? Role => CompanyMemberRole.Owner;
        public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
        public bool IsResolved => CompanyId.HasValue;
        public int RequireCompanyId() => CompanyId!.Value;
        public IDisposable Impersonate(int companyId, string? slug = null, CompanyMemberRole? role = null, IReadOnlySet<string>? permissions = null) =>
            throw new NotSupportedException();
    }

    private sealed class InMemoryRepos
    {
        private int _accountId = 100;
        private int _bankId = 100;
        public MemoryRepo<Account> Accounts { get; } = new();
        public MemoryRepo<Bank> Banks { get; } = new();
        public MemoryRepo<GeneralLedgerLine> GlLines { get; } = new();
        public MemoryRepo<GeneralLedgerHeader> GlHeaders { get; } = new();
        public MemoryRepo<CompanySetting> Settings { get; } = new();
        public int NextAccountId() => ++_accountId;
        public int NextBankId() => ++_bankId;
    }

    private sealed class MemoryRepo<T> : IRepository<T> where T : BaseEntity
    {
        private readonly List<T> _items = new();
        private int _autoId;

        public IQueryable<T> Table => _items.AsQueryable();

        public T GetById(object id)
        {
            var key = Convert.ToInt32(id);
            return _items.FirstOrDefault(i => i.Id == key)!;
        }

        public void Insert(T entity)
        {
            if (entity.Id == 0)
            {
                var max = _items.Count == 0 ? 0 : _items.Max(i => i.Id);
                entity.Id = Math.Max(max, _autoId) + 1;
                _autoId = entity.Id;
            }
            _items.Add(entity);
        }

        public System.Threading.Tasks.Task InsertAsync(T entity)
        {
            Insert(entity);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Update(T entity) { }

        public System.Threading.Tasks.Task UpdateAsync(T entity)
        {
            Update(entity);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public void Delete(T entity) => _items.Remove(entity);

        public System.Threading.Tasks.Task DeleteAsync(T entity)
        {
            Delete(entity);
            return System.Threading.Tasks.Task.CompletedTask;
        }

        public IQueryable<T> GetAllIncluding(params System.Linq.Expressions.Expression<Func<T, object>>[] includeProperties) =>
            Table;
    }
}

// Copyright (c) 2015-2026 Marvin Perez and AccountGo contributors.
// SPDX-License-Identifier: AGPL-3.0-only

#nullable enable
using System;
using System.Linq;
using Api.Data;
using Api.Tenancy;
using Core.Domain;
using Core.Domain.Financials;
using Core.Domain.Purchases;
using Core.Domain.Sales;
using Core.Domain.Security;
using Core.Entitlements;
using Dto.Banking;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Banking;
using Services.Financial;
using Xunit;

namespace AccountGo.BackendTests.Banking;

/// <summary>
/// Write-path coverage against a real SQLite database with the tenant query filter.
/// Catches EF translation failures that in-memory repos hide (e.g. StringComparison).
/// </summary>
public class BankAccountServiceEfTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TenantContext _tenant;
    private readonly ApiDbContext _db;
    private readonly BankAccountService _service;
    private readonly Mock<IEntitlementService> _entitlements = new();
    private readonly int _companyId;
    private bool _hasFinancials = true;

    public BankAccountServiceEfTests()
    {
        _connection = new SqliteConnection($"Data Source=file:bank-ef-{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();
        _tenant = new TenantContext();
        _db = new ApiDbContext(
            new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options,
            tenantContext: _tenant);
        _db.Database.EnsureCreated();

        _entitlements.Setup(e => e.EnsureCanCreate(It.IsAny<MeteredResource>(), It.IsAny<int>()));
        _entitlements.Setup(e => e.Evaluate(It.IsAny<int>(), It.IsAny<EntitlementAction>()))
            .Returns((int _, EntitlementAction action) =>
                action is EntitlementAction.Module module
                && module.AnyOf.Contains(SystemModules.Financials, StringComparer.OrdinalIgnoreCase)
                    ? new EntitlementDecision(_hasFinancials, null, null)
                    : new EntitlementDecision(true, null, null));

        var financial = new Mock<IFinancialService>(MockBehavior.Strict);
        _service = new BankAccountService(
            new EfRepository<Bank>(NullLogger<Bank>.Instance, _db),
            new EfRepository<BankTransaction>(NullLogger<BankTransaction>.Instance, _db),
            new EfRepository<Account>(NullLogger<Account>.Instance, _db),
            new EfRepository<GeneralLedgerLine>(NullLogger<GeneralLedgerLine>.Instance, _db),
            new EfRepository<GeneralLedgerHeader>(NullLogger<GeneralLedgerHeader>.Instance, _db),
            new EfRepository<CompanySetting>(NullLogger<CompanySetting>.Instance, _db),
            new EfRepository<FinancialYear>(NullLogger<FinancialYear>.Instance, _db),
            new EfRepository<SalesReceiptHeader>(NullLogger<SalesReceiptHeader>.Instance, _db),
            new EfRepository<CustomerAllocation>(NullLogger<CustomerAllocation>.Instance, _db),
            new EfRepository<SalesInvoiceHeader>(NullLogger<SalesInvoiceHeader>.Instance, _db),
            new EfRepository<VendorPayment>(NullLogger<VendorPayment>.Instance, _db),
            new EfRepository<PurchaseInvoiceHeader>(NullLogger<PurchaseInvoiceHeader>.Instance, _db),
            new EfRepository<JournalEntryHeader>(NullLogger<JournalEntryHeader>.Instance, _db),
            new EfRepository<Customer>(NullLogger<Customer>.Instance, _db),
            new EfRepository<Vendor>(NullLogger<Vendor>.Instance, _db),
            new EfRepository<Party>(NullLogger<Party>.Instance, _db),
            financial.Object,
            _entitlements.Object,
            _tenant);

        _companyId = SeedWorkspace();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public void Create_WithCaseInsensitiveDuplicateName_DoesNotThrowTranslationError()
    {
        using (_tenant.Impersonate(_companyId))
        {
            _service.Create(new CreateBankAccountRequest { Name = "BDO", Type = 1 }, includeFullNumbers: false);

            var ex = Record.Exception(() =>
                _service.Create(new CreateBankAccountRequest { Name = "bdo", Type = 1 }, includeFullNumbers: false));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("already exists", ex!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Update_WithCaseInsensitiveDuplicateName_DoesNotThrowTranslationError()
    {
        using (_tenant.Impersonate(_companyId))
        {
            var first = _service.Create(new CreateBankAccountRequest { Name = "Alpha", Type = 1 }, includeFullNumbers: false);
            var second = _service.Create(new CreateBankAccountRequest { Name = "Beta", Type = 1 }, includeFullNumbers: false);

            var ex = Record.Exception(() =>
                _service.Update(second.Id, new UpdateBankAccountRequest
                {
                    Name = "ALPHA",
                    Type = 1,
                    IsDefaultForReceipts = second.IsDefaultForReceipts,
                    IsDefaultForPayments = second.IsDefaultForPayments
                }, includeFullNumbers: false));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("already exists", ex!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("Alpha", _service.Get(first.Id, includeFullNumbers: false).Name);
        }
    }

    [Fact]
    public void Unarchive_RunsCaseInsensitiveNameCheck_WithoutTranslationError()
    {
        using (_tenant.Impersonate(_companyId))
        {
            _service.Create(new CreateBankAccountRequest { Name = "Keep", Type = 1 }, includeFullNumbers: false);
            var second = _service.Create(new CreateBankAccountRequest { Name = "Gone", Type = 1 }, includeFullNumbers: false);
            _service.Archive(second.Id, includeFullNumbers: false);

            // Force a colliding active name under the unique index so Unarchive must query NameTaken via EF.
            _db.Database.ExecuteSqlRaw(
                @"UPDATE ""Bank"" SET ""Name"" = 'GONE' WHERE ""Id"" = (
                    SELECT ""Id"" FROM ""Bank"" WHERE ""Name"" = 'Keep' AND ""CompanyId"" = {0} LIMIT 1)",
                _companyId);
            _db.ChangeTracker.Clear();

            var ex = Record.Exception(() =>
                _service.Unarchive(second.Id, includeFullNumbers: false));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("already exists", ex!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SyncBankNameFromAccount_WithCaseInsensitiveClash_DoesNotThrowTranslationError()
    {
        using (_tenant.Impersonate(_companyId))
        {
            var first = _service.Create(new CreateBankAccountRequest { Name = "One", Type = 1 }, includeFullNumbers: false);
            _service.Create(new CreateBankAccountRequest { Name = "Two", Type = 1 }, includeFullNumbers: false);

            var account = _db.Accounts.Single(a => a.Id == first.AccountId);
            account.AccountName = "TWO";

            var ex = Record.Exception(() => _service.SyncBankNameFromAccount(account));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("already exists", ex!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void LegacyCreatePath_PostingAccountId_WithDuplicateName_DoesNotThrowTranslationError()
    {
        using (_tenant.Impersonate(_companyId))
        {
            _service.Create(new CreateBankAccountRequest { Name = "Existing", Type = 1 }, includeFullNumbers: false);
            var savings = _db.Accounts.Single(a => a.AccountCode == "10112");

            var ex = Record.Exception(() =>
                _service.Create(new CreateBankAccountRequest
                {
                    Name = "existing",
                    Type = 1,
                    PostingAccountId = savings.Id
                }, includeFullNumbers: false));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("already exists", ex!.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("could not be translated", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private int SeedWorkspace()
    {
        // EnsureCreated already seeds Company Id = 1 (SeeData).
        var companyId = _db.Companies.Select(c => c.Id).Single();
        var assets = _db.AccountClasses.FirstOrDefault(c => c.NormalBalance == "Dr")
            ?? _db.AccountClasses.Add(new AccountClass { Name = "Assets", NormalBalance = "Dr" }).Entity;
        if (assets.Id == 0)
            _db.SaveChanges();

        using (_tenant.Impersonate(companyId))
        {
            var current = new Account
            {
                CompanyId = companyId,
                AccountClassId = assets.Id,
                AccountCode = "10100",
                AccountName = "Current Assets",
                DrOrCrSide = DrOrCrSide.Dr
            };
            _db.Accounts.Add(current);
            _db.SaveChanges();

            var cash = new Account
            {
                CompanyId = companyId,
                AccountClassId = assets.Id,
                ParentAccountId = current.Id,
                AccountCode = "10110",
                AccountName = "Cash in Hand and in Bank",
                IsCash = true,
                DrOrCrSide = DrOrCrSide.Dr
            };
            _db.Accounts.Add(cash);
            _db.SaveChanges();

            foreach (var (code, name) in new[]
                     {
                         ("10111", "Regular Checking Account"),
                         ("10112", "Savings"),
                         ("10113", "Cash in Hand")
                     })
            {
                _db.Accounts.Add(new Account
                {
                    CompanyId = companyId,
                    AccountClassId = assets.Id,
                    ParentAccountId = cash.Id,
                    AccountCode = code,
                    AccountName = name,
                    DrOrCrSide = DrOrCrSide.Dr
                });
            }

            if (!_db.CompanySettings.Any(s => s.CompanyId == companyId))
            {
                _db.CompanySettings.Add(new CompanySetting
                {
                    CompanyId = companyId,
                    CoaTemplateId = CoaTemplates.Legacy
                });
            }
            _db.SaveChanges();
        }

        return companyId;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Api.Controllers;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>Phase 4: policy lock after the first journal, safe edits while books are empty, books-start posting guard, checklist dismissal.</summary>
public class CompanyPolicyLocksTests : IDisposable
{
    private static DateTime D(string iso) => DateTime.ParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly TenantContext _tenant = new();
    private readonly Company _company;

    public CompanyPolicyLocksTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, tenantContext: _tenant);
        _db.Database.EnsureCreated();
        _company = new Company { Name = "Lock Co", Slug = "lock-co" };
        _db.Companies.Add(_company);
        _db.SaveChanges();
        _tenant.Impersonate(_company.Id, _company.Slug, CompanyMemberRole.Owner);
        var provisioning = new CompanyProvisioningService(_db, _tenant);
        provisioning.Provision(_company, new CompanyProvisionOptions { BooksStartDate = D("2026-01-01") });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private OrganizationsController Controller()
    {
        var c = new OrganizationsController(_tenant, _db, new WorkspaceService(_db, new CompanyProvisioningService(_db, _tenant)));
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "u") }, "test"))
            }
        };
        return c;
    }

    private void FreezeLedger()
    {
        _db.GeneralLedgerHeaders.Add(new GeneralLedgerHeader { CompanyId = _company.Id, Date = D("2026-02-01"), Description = "first" });
        _db.SaveChanges();
    }

    [Fact]
    public void Update_WhileBooksAreEmpty_ReplacesTheActiveFiscalYear()
    {
        var result = Controller().UpdateAccountingPolicy(new OrganizationsController.AccountingPolicyModel
        {
            FiscalYearPattern = FiscalYearPattern.AprMar,
            BooksStartDate = D("2026-02-15"),
        });

        Assert.IsType<OkObjectResult>(result);
        var active = _db.FinancialYears.Where(f => f.IsActive).ToList();
        var only = Assert.Single(active);
        Assert.Equal("FY2025-26", only.FiscalYearCode);
        Assert.Equal(FiscalYearPattern.AprMar, _db.CompanySettings.Single().FiscalYearPattern);
    }

    [Fact]
    public void Update_AfterFirstJournal_RejectsFrozenFieldsWith409()
    {
        FreezeLedger();

        var result = Controller().UpdateAccountingPolicy(new OrganizationsController.AccountingPolicyModel
        {
            ReportingFramework = ReportingFramework.UsGaapPrivate,
        });

        Assert.Equal(409, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal(ReportingFramework.IfrsForSmes, _db.CompanySettings.Single().ReportingFramework);
    }

    [Fact]
    public void Update_AfterFirstJournal_StillAllowsTaxRegimeAndInventoryMethod()
    {
        FreezeLedger();

        var result = Controller().UpdateAccountingPolicy(new OrganizationsController.AccountingPolicyModel
        {
            TaxRegime = TaxRegime.VatGst,
            InventoryCostingMethod = InventoryCostingMethod.Fifo,
        });

        Assert.IsType<OkObjectResult>(result);
        var setting = _db.CompanySettings.Single();
        Assert.Equal(TaxRegime.VatGst, setting.TaxRegime);
        Assert.Equal(InventoryCostingMethod.Fifo, setting.InventoryCostingMethod);
    }

    [Fact]
    public void Update_RejectsInvalidCombination()
    {
        var result = Controller().UpdateAccountingPolicy(new OrganizationsController.AccountingPolicyModel
        {
            ReportingFramework = ReportingFramework.UsGaapPrivate,
            AccountingBasis = AccountingBasis.Cash,
        });

        Assert.Equal(400, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public void DismissChecklist_PersistsAndIsIdempotent()
    {
        var c = Controller();
        c.DismissOnboardingChecklist();
        var first = _db.CompanySettings.Single().OnboardingChecklistDismissedAt;
        c.DismissOnboardingChecklist();

        Assert.NotNull(first);
        Assert.Equal(first, _db.CompanySettings.Single().OnboardingChecklistDismissedAt);
    }

    private Services.Financial.FinancialService GuardedFinancialService(DateTime booksStart)
    {
        var settings = new List<CompanySetting> { new() { CompanyId = 1, BooksStartDate = booksStart } };
        var repo = new Mock<IRepository<CompanySetting>>();
        repo.Setup(r => r.Table).Returns(settings.AsQueryable());
        return new Services.Financial.FinancialService(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            companySettingRepo: repo.Object);
    }

    [Fact]
    public void CreateGeneralLedgerHeader_BeforeBooksStart_Throws()
    {
        var service = GuardedFinancialService(D("2026-04-01"));

        Assert.Throws<BooksStartViolationException>(() =>
            service.CreateGeneralLedgerHeader(DocumentTypes.JournalEntry, D("2026-03-31"), "early"));
    }

    [Theory]
    [InlineData("2026-04-01")]
    [InlineData("2026-09-30")]
    public void CreateGeneralLedgerHeader_OnOrAfterBooksStart_Succeeds(string date)
    {
        var service = GuardedFinancialService(D("2026-04-01"));

        var header = service.CreateGeneralLedgerHeader(DocumentTypes.JournalEntry, D(date), "ok");

        Assert.Equal(D(date), header.Date);
    }
}

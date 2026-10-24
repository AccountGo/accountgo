using System;
using System.Linq;
using System.Security.Claims;
using Api.Controllers;
using Api.Data;
using Api.Service;
using Api.Tenancy;
using Core.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Services.Administration;
using Xunit;

namespace AccountGo.ServicesTests;

/// <summary>
/// POST /api/organizations: creating a workspace saves the company and makes the caller its Owner.
/// </summary>
public class SignUpAndOrganizationTests : IDisposable
{
    private const string CallerId = "caller-user-id";

    private readonly SqliteConnection _connection;
    private readonly ApiDbContext _db;
    private readonly Mock<IAdministrationService> _mockAdminService = new();
    private readonly TenantContext _tenantContext = new();

    public SignUpAndOrganizationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _db = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options, tenantContext: _tenantContext);
        _db.Database.EnsureCreated();

        _mockAdminService.Setup(s => s.GetCompanyBySlug(It.IsAny<string>()))
            .Returns<string>(slug => _db.Companies.FirstOrDefault(c => c.Slug == slug));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private OrganizationsController NewController()
    {
        var controller = new OrganizationsController(_tenantContext, _db,
            new WorkspaceService(_db, new CompanyProvisioningService(_db, _tenantContext)));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, CallerId) }, "test"))
            }
        };
        return controller;
    }

    [Fact]
    public void CreateCompany_WhenNameIsEmpty_ReturnsBadRequest()
    {
        var result = NewController().Create(new OrganizationsController.CreateOrganizationRequest { Name = "" });

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Company Name Required", problem.Title);
    }

    [Fact]
    public void CreateCompany_WithValidData_ReturnsCreated_And_Caller_Is_Owner()
    {
        var result = NewController().Create(new OrganizationsController.CreateOrganizationRequest
        {
            Name = "Pacific Rim Trading Inc.",
            Slug = "pacific-rim-trading",
            Plan = "accounting",
            Currency = "PHP"
        });

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);

        var company = _db.Companies.Single(c => c.Slug == "pacific-rim-trading");
        Assert.Equal("accounting", company.Plan);

        var member = _db.CompanyMembers.Single(m => m.CompanyId == company.Id);
        Assert.Equal(CallerId, member.UserId);
        Assert.Equal(CompanyMemberRole.Owner, member.Role);
        Assert.Equal(CompanyMemberStatus.Active, member.Status);
    }

    [Fact]
    public void CreateCompany_WhenSlugAlreadyExists_ReturnsConflict()
    {
        _db.Companies.Add(new Company { Name = "Existing Workspace", Slug = "existing-workspace" });
        _db.SaveChanges();
        var before = _db.Companies.Count();

        var result = NewController().Create(new OrganizationsController.CreateOrganizationRequest
        {
            Name = "Another Workspace",
            Slug = "existing-workspace"
        });

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Workspace Slug Already Taken", problem.Title);
        Assert.Equal(before, _db.Companies.Count());
    }

    [Fact]
    public void CreateCompany_WithSpecialCharactersInSlug_SanitizesCorrectly()
    {
        var result = NewController().Create(new OrganizationsController.CreateOrganizationRequest
        {
            Name = "Apex & Co. / Logistics!",
            Slug = "apex & co. / logistics!"
        });

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.NotNull(_db.Companies.SingleOrDefault(c => c.Slug == "apex-co-logistics"));
    }

    private static OrganizationsController.CreateOrganizationRequest FullRequest(string slug) => new()
    {
        Name = "Acme Studio", Slug = slug, Currency = "USD",
        ReportingFramework = ReportingFramework.UsGaapPrivate,
        AccountingBasis = AccountingBasis.Accrual,
        FiscalYearPattern = FiscalYearPattern.Calendar,
        BooksStartDate = new DateTime(2026, 2, 15),
        TaxRegime = TaxRegime.None,
        IndustryTemplate = IndustryTemplate.Services,
        InventoryCostingMethod = InventoryCostingMethod.None
    };

    private static DateTime D(string value) => DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(FiscalYearPattern.Calendar, null, "2026-02-15", "2026-01-01", "2026-12-31", "FY2026")]
    [InlineData(FiscalYearPattern.AprMar, null, "2026-05-01", "2026-04-01", "2027-03-31", "FY2026-27")]
    [InlineData(FiscalYearPattern.AprMar, null, "2026-02-15", "2025-04-01", "2026-03-31", "FY2025-26")]
    [InlineData(FiscalYearPattern.Custom, 10, "2026-10-24", "2026-10-01", "2027-09-30", "FY2026-27")]
    public void CreateCompany_DerivesFiscalYearFromPatternAndBooksStart(
        FiscalYearPattern pattern, int? month, string booksStart, string start, string end, string code)
    {
        var request = FullRequest("fy-" + code.ToLowerInvariant());
        request.FiscalYearPattern = pattern;
        request.FiscalYearStartMonth = month;
        request.BooksStartDate = D(booksStart);

        var result = Assert.IsType<ObjectResult>(NewController().Create(request));
        Assert.True(result.StatusCode == StatusCodes.Status201Created, (result.Value as ProblemDetails)?.Detail ?? (result.Value as ProblemDetails)?.Title);

        var company = _db.Companies.Single(c => c.Slug == request.Slug);
        var year = _db.FinancialYears.IgnoreQueryFilters().Single(f => f.CompanyId == company.Id);
        Assert.Equal(code, year.FiscalYearCode);
        Assert.Equal(D(start), year.StartDate.Date);
        Assert.Equal(D(end), year.EndDate.Date);
        Assert.True(year.IsActive);
        var setting = _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(D(booksStart), setting.BooksStartDate);
        Assert.Equal(pattern, setting.FiscalYearPattern);
    }

    [Fact]
    public void CreateCompany_PersistsPolicyAndTemplateId_AndSeedsNoJournals()
    {
        var request = FullRequest("acme-studio");
        request.LegalName = "Acme Studio LLC";
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(NewController().Create(request)).StatusCode);

        var company = _db.Companies.Single(c => c.Slug == "acme-studio");
        var setting = _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        Assert.Equal(ReportingFramework.UsGaapPrivate, setting.ReportingFramework);
        Assert.Equal("services-sme", setting.CoaTemplateId);
        Assert.Equal("Acme Studio LLC", setting.LegalName);
        Assert.False(_db.GeneralLedgerHeaders.IgnoreQueryFilters().Any(h => h.CompanyId == company.Id));
        Assert.False(new CompanyAccountingPolicyService(_db).IsLedgerFrozen(company.Id));
    }

    [Fact]
    public void CreateCompany_LegacyPayload_UsesDocumentedDefaults()
    {
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(NewController().Create(
            new OrganizationsController.CreateOrganizationRequest { Name = "Legacy Co", Slug = "legacy-co" })).StatusCode);

        var company = _db.Companies.Single(c => c.Slug == "legacy-co");
        var setting = _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id);
        var year = _db.FinancialYears.IgnoreQueryFilters().Single(f => f.CompanyId == company.Id);
        Assert.Equal(ReportingFramework.IfrsForSmes, setting.ReportingFramework);
        Assert.Equal(AccountingBasis.Accrual, setting.AccountingBasis);
        Assert.Equal(year.StartDate.Date, setting.BooksStartDate);
        Assert.Equal(new DateTime(DateTime.UtcNow.Year, 1, 1), year.StartDate.Date);
    }

    [Fact]
    public void CreateCompany_CashBasisFramework_DefaultsToCashBasis()
    {
        var request = FullRequest("cash-co");
        request.ReportingFramework = ReportingFramework.CashBasis;
        request.AccountingBasis = null;
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(NewController().Create(request)).StatusCode);
        var company = _db.Companies.Single(c => c.Slug == "cash-co");
        Assert.Equal(AccountingBasis.Cash, _db.CompanySettings.IgnoreQueryFilters().Single(s => s.CompanyId == company.Id).AccountingBasis);
    }

    [Theory]
    [InlineData("cash-accrual")]
    [InlineData("ifrs-cash")]
    [InlineData("custom-no-month")]
    [InlineData("custom-bad-month")]
    [InlineData("bad-tax")]
    [InlineData("bad-costing")]
    [InlineData("bad-pattern")]
    [InlineData("bad-date")]
    public void CreateCompany_InvalidPolicy_ReturnsBadRequest_AndCreatesNothing(string scenario)
    {
        var request = FullRequest("bad-" + scenario);
        switch (scenario)
        {
            case "cash-accrual": request.ReportingFramework = ReportingFramework.CashBasis; request.AccountingBasis = AccountingBasis.Accrual; break;
            case "ifrs-cash": request.ReportingFramework = ReportingFramework.Ifrs; request.AccountingBasis = AccountingBasis.Cash; break;
            case "custom-no-month": request.FiscalYearPattern = FiscalYearPattern.Custom; break;
            case "custom-bad-month": request.FiscalYearPattern = FiscalYearPattern.Custom; request.FiscalYearStartMonth = 13; break;
            case "bad-tax": request.TaxRegime = (TaxRegime)99; break;
            case "bad-costing": request.InventoryCostingMethod = (InventoryCostingMethod)3; break;
            case "bad-pattern": request.FiscalYearPattern = (FiscalYearPattern)42; break;
            case "bad-date": request.BooksStartDate = new DateTime(1850, 1, 1); break;
        }

        var result = Assert.IsType<ObjectResult>(NewController().Create(request));

        Assert.Equal(StatusCodes.Status400BadRequest, result.StatusCode);
        Assert.Empty(_db.Companies.Where(c => c.Slug == request.Slug));
    }

    [Theory]
    [InlineData(TaxRegime.None, "NONE", true)]
    [InlineData(TaxRegime.SalesTax, "SALESTAX", false)]
    [InlineData(TaxRegime.VatGst, "VAT", false)]
    public void CreateCompany_SeedsTaxByRegime(TaxRegime regime, string code, bool active)
    {
        var request = FullRequest("tax-" + code.ToLowerInvariant());
        request.TaxRegime = regime;
        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(NewController().Create(request)).StatusCode);

        var company = _db.Companies.Single(c => c.Slug == request.Slug);
        var tax = _db.Taxes.IgnoreQueryFilters().Single(t => t.CompanyId == company.Id);
        Assert.Equal(code, tax.TaxCode);
        Assert.Equal(active, tax.IsActive);
        Assert.Equal(0, tax.Rate);
    }

    [Fact]
    public void IsLedgerFrozen_TrueOnceAJournalExists()
    {
        var company = new Company { Name = "Frozen Co", Slug = "frozen-co" };
        _db.Companies.Add(company);
        _db.SaveChanges();
        using var _ = _tenantContext.Impersonate(company.Id, company.Slug, CompanyMemberRole.Owner);
        _db.GeneralLedgerHeaders.Add(new Core.Domain.Financials.GeneralLedgerHeader { CompanyId = company.Id, Date = DateTime.UtcNow, Description = "x" });
        _db.SaveChanges();

        Assert.True(new CompanyAccountingPolicyService(_db).IsLedgerFrozen(company.Id));
    }
}

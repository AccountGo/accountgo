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
}

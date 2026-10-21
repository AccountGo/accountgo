using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Api.Controllers;
using Core.Data;
using Core.Domain;
using Core.Domain.Security;
using Dto.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Services.Administration;
using Services.Financial;
using Services.Inventory;
using Services.Purchasing;
using Services.Sales;
using Services.Security;
using Xunit;

namespace AccountGo.ServicesTests;

public class SignUpAndOrganizationTests
{
    private readonly Mock<IAdministrationService> _mockAdminService;
    private readonly Mock<IFinancialService> _mockFinancialService;
    private readonly Mock<ISalesService> _mockSalesService;
    private readonly Mock<IPurchasingService> _mockPurchasingService;
    private readonly Mock<IInventoryService> _mockInventoryService;
    private readonly Mock<ISecurityService> _mockSecurityService;

    private readonly List<Company> _companies = new();

    public SignUpAndOrganizationTests()
    {
        _mockAdminService = new Mock<IAdministrationService>();
        _mockFinancialService = new Mock<IFinancialService>();
        _mockSalesService = new Mock<ISalesService>();
        _mockPurchasingService = new Mock<IPurchasingService>();
        _mockInventoryService = new Mock<IInventoryService>();
        _mockSecurityService = new Mock<ISecurityService>();

        _mockAdminService.Setup(s => s.GetCompanies()).Returns(() => _companies);
        _mockAdminService.Setup(s => s.GetCompanyBySlug(It.IsAny<string>()))
            .Returns<string>(slug => _companies.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase)));

        _mockAdminService.Setup(s => s.SaveCompany(It.IsAny<Company>()))
            .Callback<Company>(c =>
            {
                if (c.Id == 0)
                {
                    c.Id = _companies.Count + 1;
                    _companies.Add(c);
                }
            });
    }

    [Fact]
    public void CreateCompany_WhenNameIsEmpty_ReturnsBadRequest()
    {
        var controller = new AdministrationController(
            _mockAdminService.Object,
            _mockFinancialService.Object,
            _mockSalesService.Object,
            _mockPurchasingService.Object,
            _mockInventoryService.Object,
            _mockSecurityService.Object,
            null!);

        var result = controller.CreateCompany(new AdministrationController.CreateCompanyRequest { Name = "" });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Company Name Required", problem.Title);
    }

    [Fact]
    public void CreateCompany_WithValidData_ReturnsCreatedAndUniqueSlug()
    {
        var controller = new AdministrationController(
            _mockAdminService.Object,
            _mockFinancialService.Object,
            _mockSalesService.Object,
            _mockPurchasingService.Object,
            _mockInventoryService.Object,
            _mockSecurityService.Object,
            null!);

        var request = new AdministrationController.CreateCompanyRequest
        {
            Name = "Pacific Rim Trading Inc.",
            Slug = "pacific-rim-trading",
            Plan = "accounting",
            Currency = "PHP"
        };

        var result = controller.CreateCompany(request);

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Single(_companies);
        Assert.Equal("pacific-rim-trading", _companies[0].Slug);
        Assert.Equal("accounting", _companies[0].Plan);
    }

    [Fact]
    public void CreateCompany_WhenSlugAlreadyExists_ReturnsBadRequest()
    {
        _companies.Add(new Company
        {
            Id = 1,
            Name = "Existing Workspace",
            Slug = "existing-workspace"
        });

        var controller = new AdministrationController(
            _mockAdminService.Object,
            _mockFinancialService.Object,
            _mockSalesService.Object,
            _mockPurchasingService.Object,
            _mockInventoryService.Object,
            _mockSecurityService.Object,
            null!);

        var request = new AdministrationController.CreateCompanyRequest
        {
            Name = "Another Workspace",
            Slug = "existing-workspace"
        };

        var result = controller.CreateCompany(request);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Workspace Slug Already Taken", problem.Title);
    }

    [Fact]
    public void CreateCompany_WithSpecialCharactersInSlug_SanitizesCorrectly()
    {
        var controller = new AdministrationController(
            _mockAdminService.Object,
            _mockFinancialService.Object,
            _mockSalesService.Object,
            _mockPurchasingService.Object,
            _mockInventoryService.Object,
            _mockSecurityService.Object,
            null!);

        var request = new AdministrationController.CreateCompanyRequest
        {
            Name = "Apex & Co. / Logistics!",
            Slug = "apex & co. / logistics!"
        };

        var result = controller.CreateCompany(request);

        var createdResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, createdResult.StatusCode);
        Assert.Single(_companies);
        Assert.Equal("apex-co-logistics", _companies[0].Slug);
    }
}

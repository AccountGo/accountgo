using Dto;
using Dto.Administration;
using System.ComponentModel.DataAnnotations;

namespace GoodBooks.ServicesTests.Dto.Administration;

/// <summary>
/// Unit tests for the Company DTO class.
/// Tests validation, properties, and data integrity.
/// </summary>
public class CompanyDtoTests
{
    [Fact]
    public void Company_DefaultInitialization_ShouldHaveNullProperties()
    {
        // Arrange & Act
        var company = new Company();

        // Assert
        Assert.Equal(0, company.Id);
        Assert.Null(company.CompanyCode);
        Assert.Null(company.Name);
        Assert.Null(company.ShortName);
        Assert.Null(company.CRA);
        Assert.Null(company.Logo);
        Assert.Null(company.ModifiedBy);
    }

    [Fact]
    public void Company_SetAllProperties_ShouldStoreValues()
    {
        // Arrange
        var company = new Company();
        var code = "COMP001";
        var name = "Test Company Inc.";
        var shortName = "TCI";
        var cra = "123456789";
        var logo = new byte[] { 1, 2, 3 };

        // Act
        company.Id = 1;
        company.CompanyCode = code;
        company.Name = name;
        company.ShortName = shortName;
        company.CRA = cra;
        company.Logo = logo;
        company.ModifiedBy = "Admin";

        // Assert
        Assert.Equal(1, company.Id);
        Assert.Equal(code, company.CompanyCode);
        Assert.Equal(name, company.Name);
        Assert.Equal(shortName, company.ShortName);
        Assert.Equal(cra, company.CRA);
        Assert.NotNull(company.Logo);
        Assert.Equal(3, company.Logo.Length);
        Assert.Equal("Admin", company.ModifiedBy);
    }

    [Fact]
    public void Company_Logo_CanBeNull()
    {
        // Arrange
        var company = new Company();

        // Act
        company.Logo = null;

        // Assert
        Assert.Null(company.Logo);
    }

    [Fact]
    public void Company_Logo_CanStoreByteArray()
    {
        // Arrange
        var company = new Company();
        var largeByteArray = new byte[1024];

        // Act
        company.Logo = largeByteArray;

        // Assert
        Assert.NotNull(company.Logo);
        Assert.Equal(1024, company.Logo.Length);
    }

    [Theory]
    [InlineData("COMP001", "Company One", "CO1", "111111111")]
    [InlineData("COMP002", "Company Two", "CO2", "222222222")]
    [InlineData("COMP003", "Company Three Inc.", "C3", "333333333")]
    public void Company_WithValidData_ShouldInitializeCorrectly(
        string code, string name, string shortName, string cra)
    {
        // Arrange & Act
        var company = new Company
        {
            Id = 1,
            CompanyCode = code,
            Name = name,
            ShortName = shortName,
            CRA = cra
        };

        // Assert
        Assert.Equal(1, company.Id);
        Assert.Equal(code, company.CompanyCode);
        Assert.Equal(name, company.Name);
        Assert.Equal(shortName, company.ShortName);
        Assert.Equal(cra, company.CRA);
    }

    [Fact]
    public void Company_ShouldInheritFromBaseDto()
    {
        // Arrange & Act
        var company = new Company { Id = 1, ModifiedBy = "User" };

        // Assert
        Assert.IsAssignableFrom<BaseDto>(company);
        Assert.Equal(1, company.Id);
        Assert.Equal("User", company.ModifiedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VeryLongCompanyCodeThatMightExceedExpectedLength")]
    public void Company_StringProperties_ShouldAcceptAnyString(string value)
    {
        // Arrange & Act
        var company = new Company { CompanyCode = value };

        // Assert
        Assert.Equal(value, company.CompanyCode);
    }
}

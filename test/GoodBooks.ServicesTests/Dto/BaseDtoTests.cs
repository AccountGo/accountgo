global using Dto;
using Xunit;

namespace GoodBooks.ServicesTests.Dto;

/// <summary>
/// Unit tests for the BaseDto class.
/// Tests the common properties and behavior of all DTOs.
/// </summary>
public class BaseDtoTests
{
    [Fact]
    public void BaseDto_DefaultInitialization_ShouldHaveDefaultValues()
    {
        // Arrange & Act
        var dto = new TestDto();

        // Assert
        Assert.Equal(0, dto.Id);
        Assert.Null(dto.ModifiedBy);
    }

    [Fact]
    public void BaseDto_SetId_ShouldStoreValue()
    {
        // Arrange
        var dto = new TestDto();
        var expectedId = 42;

        // Act
        dto.Id = expectedId;

        // Assert
        Assert.Equal(expectedId, dto.Id);
    }

    [Fact]
    public void BaseDto_SetModifiedBy_ShouldStoreValue()
    {
        // Arrange
        var dto = new TestDto();
        var modifiedBy = "TestUser";

        // Act
        dto.ModifiedBy = modifiedBy;

        // Assert
        Assert.Equal(modifiedBy, dto.ModifiedBy);
    }

    [Fact]
    public void BaseDto_SetProperties_ShouldAllowMultipleModifications()
    {
        // Arrange
        var dto = new TestDto();

        // Act
        dto.Id = 1;
        dto.ModifiedBy = "User1";
        dto.Id = 2;
        dto.ModifiedBy = "User2";

        // Assert
        Assert.Equal(2, dto.Id);
        Assert.Equal("User2", dto.ModifiedBy);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(int.MaxValue)]
    public void BaseDto_Id_ShouldSupportVariousIntegerValues(int id)
    {
        // Arrange & Act
        var dto = new TestDto { Id = id };

        // Assert
        Assert.Equal(id, dto.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("TestUser")]
    [InlineData("user@example.com")]
    [InlineData("User with spaces")]
    public void BaseDto_ModifiedBy_ShouldSupportVariousStringValues(string modifiedBy)
    {
        // Arrange & Act
        var dto = new TestDto { ModifiedBy = modifiedBy };

        // Assert
        Assert.Equal(modifiedBy, dto.ModifiedBy);
    }
}

/// <summary>
/// Concrete implementation of BaseDto for testing purposes.
/// </summary>
public class TestDto : BaseDto
{
}

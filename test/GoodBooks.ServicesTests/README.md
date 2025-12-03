# GoodBooks.ServicesTests

Unit tests for GoodBooks backend services using **xUnit** framework and **Moq** for mocking.

**Current Test Coverage:**
- BaseServiceTests: 8 tests (repository operations)
- BaseDtoTests: 7 tests (base DTO class)
- CompanyDtoTests: 10 tests (Company DTO example)
- **Total: 31 tests**

## Quick Start

### Run All Tests
```bash
dotnet test test/GoodBooks.ServicesTests/
```

### Run Tests with Watch Mode (auto-run on file changes)
```bash
cd test/GoodBooks.ServicesTests && dotnet watch test
```

### Run Specific Test Class
```bash
dotnet test test/GoodBooks.ServicesTests/ --filter "ClassName=BaseServiceTests"
```

### Run with Verbose Output
```bash
dotnet test test/GoodBooks.ServicesTests/ --verbosity normal
```

## Project Structure

```
GoodBooks.ServicesTests/
├── BaseServiceTests.cs              # 8 tests for repository operations
├── Dto/
│   ├── BaseDtoTests.cs             # 7 tests for BaseDto
│   └── Administration/
│       └── CompanyDtoTests.cs      # 10 tests for Company DTO
├── Fixtures/
│   └── MockRepositoryFixtures.cs   # Reusable mock factories
└── GoodBooks.ServicesTests.csproj
```

## Test Patterns

All tests use the **AAA Pattern** (Arrange-Act-Assert):

```csharp
[Fact]
public void TestName_Should_ExpectedBehavior()
{
    // Arrange: Set up test data
    var mockRepo = new Mock<IRepository<T>>();
    
    // Act: Execute code
    var result = service.DoSomething();
    
    // Assert: Verify results
    Assert.Equal(expectedValue, result);
}
```

## Adding New Tests

Create test files following the same pattern as existing tests. Use `MockRepositoryFixtures` for creating mock repositories:

```csharp
using Xunit;
using GoodBooks.ServicesTests.Fixtures;

namespace GoodBooks.ServicesTests.Services;

public class NewServiceTests
{
    [Fact]
    public void MethodName_Should_ExpectedBehavior()
    {
        // Arrange
        var mockRepo = MockRepositoryFixtures.CreateEmptyRepository<T>();
        
        // Act
        var result = mockRepo.Object.GetAll();
        
        // Assert
        Assert.NotNull(result);
    }
}
```

## Test Fixtures

Reusable mock factories in `MockRepositoryFixtures.cs`:
- `CreateEmptyRepository<T>()` - Empty mock
- `CreateRepositoryWithData<T>()` - Mock with sample data
- `CreateSequenceNumberRepository()` - Pre-configured sequence mock
- Sample data creators: `CreateSampleBank()`, `CreateSampleSequence()`, etc.
- [ ] Financial Services tests
- [ ] Inventory Services tests
- [ ] Purchasing Services tests
- [ ] Sales Services tests
- [ ] Security Services tests
- [ ] Tax System Services tests
- [ ] Integration tests with real database
- [ ] Performance benchmarks

### Contributing
When adding new tests:
1. Follow the existing structure and naming conventions
2. Use appropriate fixtures for mock creation
3. Include XML documentation comments
4. Ensure all tests pass locally before committing
5. Aim for >80% code coverage

## Troubleshooting

### Tests Not Discovered
```bash
dotnet test test/GoodBooks.ServicesTests/ --verbosity diagnostic
```

### Mock Issues
Ensure repositories are properly configured:
```csharp
_mockRepo.Setup(r => r.Table).Returns(data.AsQueryable());
_mockRepo.Setup(r => r.Insert(It.IsAny<T>()));
```

### Async Test Issues
Use `async Task` with `await` for async tests:
```csharp
[Fact]
public async Task AsyncTest_Should_Work()
{
    var result = await service.GetDataAsync();
    Assert.NotNull(result);
}
```

## Resources

- [xUnit Documentation](https://xunit.net/)
- [Moq Quick Start](https://github.com/moq/moq4/wiki/Quickstart)
- [Unit Testing Best Practices](https://docs.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)

## License

Copyright (c) AccountGo. All rights reserved.

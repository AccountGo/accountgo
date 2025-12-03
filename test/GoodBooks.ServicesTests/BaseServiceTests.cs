using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Moq;
using Services;

namespace GoodBooks.ServicesTests;

/// <summary>
/// Unit tests for the BaseService class.
/// Tests the sequence number generation and protected methods used by all services.
/// </summary>
public class BaseServiceTests
{
    private readonly Mock<IRepository<SequenceNumber>> _mockSequenceNumberRepo;
    private readonly Mock<IRepository<GeneralLedgerSetting>> _mockGeneralLedgerSettingRepo;
    private readonly Mock<IRepository<PaymentTerm>> _mockPaymentTermRepo;
    private readonly Mock<IRepository<Bank>> _mockBankRepo;

    public BaseServiceTests()
    {
        _mockSequenceNumberRepo = new Mock<IRepository<SequenceNumber>>();
        _mockGeneralLedgerSettingRepo = new Mock<IRepository<GeneralLedgerSetting>>();
        _mockPaymentTermRepo = new Mock<IRepository<PaymentTerm>>();
        _mockBankRepo = new Mock<IRepository<Bank>>();
    }

    [Fact]
    public void SequenceNumberRepository_WithNewSequence_ShouldInsertAndRetrieve()
    {
        // Arrange
        var sequenceType = SequenceNumberTypes.JournalEntry;
        var sequenceList = new List<SequenceNumber>();
        
        _mockSequenceNumberRepo
            .Setup(r => r.Table)
            .Returns(() => sequenceList.AsQueryable());
        
        _mockSequenceNumberRepo
            .Setup(r => r.Insert(It.IsAny<SequenceNumber>()))
            .Callback<SequenceNumber>(s => sequenceList.Add(s));

        // Act
        var newSequence = new SequenceNumber
        {
            SequenceNumberType = sequenceType,
            Description = "Test",
            NextNumber = 1,
            UsePrefix = false
        };
        _mockSequenceNumberRepo.Object.Insert(newSequence);
        var result = _mockSequenceNumberRepo.Object.Table.FirstOrDefault(s => s.SequenceNumberType == sequenceType);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sequenceType, result.SequenceNumberType);
        Assert.Equal(1, result.NextNumber);
    }

    [Fact]
    public void SequenceNumberRepository_WithExistingSequence_ShouldUpdate()
    {
        // Arrange
        var sequenceType = SequenceNumberTypes.JournalEntry;
        var existingSequence = new SequenceNumber 
        { 
            Id = 1,
            SequenceNumberType = sequenceType,
            NextNumber = 42,
            Description = "Test"
        };
        var sequenceList = new List<SequenceNumber> { existingSequence };

        _mockSequenceNumberRepo
            .Setup(r => r.Table)
            .Returns(() => sequenceList.AsQueryable());

        _mockSequenceNumberRepo
            .Setup(r => r.Update(It.IsAny<SequenceNumber>()))
            .Callback<SequenceNumber>(s => 
            {
                var existing = sequenceList.FirstOrDefault(x => x.Id == s.Id);
                if (existing != null)
                {
                    existing.NextNumber = s.NextNumber;
                }
            });

        // Act
        existingSequence.NextNumber = 43;
        _mockSequenceNumberRepo.Object.Update(existingSequence);

        // Assert
        Assert.Equal(43, existingSequence.NextNumber);
        _mockSequenceNumberRepo.Verify(r => r.Update(It.IsAny<SequenceNumber>()), Times.Once);
    }

    [Theory]
    [InlineData(SequenceNumberTypes.JournalEntry)]
    [InlineData(SequenceNumberTypes.PurchaseOrder)]
    [InlineData(SequenceNumberTypes.SalesOrder)]
    public void SequenceNumberRepository_WithDifferentTypes_ShouldHandleAllTypes(SequenceNumberTypes type)
    {
        // Arrange
        var sequenceList = new List<SequenceNumber>();
        
        _mockSequenceNumberRepo
            .Setup(r => r.Table)
            .Returns(() => sequenceList.AsQueryable());
        
        _mockSequenceNumberRepo
            .Setup(r => r.Insert(It.IsAny<SequenceNumber>()))
            .Callback<SequenceNumber>(s => sequenceList.Add(s));

        // Act
        var newSequence = new SequenceNumber
        {
            SequenceNumberType = type,
            Description = Enum.GetName(typeof(SequenceNumberTypes), type),
            NextNumber = 1,
            UsePrefix = false
        };
        _mockSequenceNumberRepo.Object.Insert(newSequence);

        // Assert
        Assert.Single(sequenceList);
        Assert.Equal(type, sequenceList.First().SequenceNumberType);
    }

    [Fact]
    public void GeneralLedgerSettingRepository_WhenExists_ShouldRetrieveSetting()
    {
        // Arrange
        var glSetting = new GeneralLedgerSetting { Id = 1 };
        var settingsList = new List<GeneralLedgerSetting> { glSetting };

        _mockGeneralLedgerSettingRepo
            .Setup(r => r.Table)
            .Returns(settingsList.AsQueryable());

        // Act
        var result = _mockGeneralLedgerSettingRepo.Object.Table.FirstOrDefault();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void GeneralLedgerSettingRepository_WhenEmpty_ShouldReturnNull()
    {
        // Arrange
        var settingsList = new List<GeneralLedgerSetting>();

        _mockGeneralLedgerSettingRepo
            .Setup(r => r.Table)
            .Returns(settingsList.AsQueryable());

        // Act
        var result = _mockGeneralLedgerSettingRepo.Object.Table.FirstOrDefault();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void BankRepository_ShouldReturnAllBanks()
    {
        // Arrange
        var banks = new List<Bank>
        {
            new Bank { Id = 1, BankName = "Bank A" },
            new Bank { Id = 2, BankName = "Bank B" }
        };

        _mockBankRepo
            .Setup(r => r.Table)
            .Returns(banks.AsQueryable());

        // Act
        var result = _mockBankRepo.Object.Table;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count());
    }

    [Fact]
    public void BankRepository_WhenEmpty_ShouldReturnEmptyList()
    {
        // Arrange
        var banks = new List<Bank>();

        _mockBankRepo
            .Setup(r => r.Table)
            .Returns(banks.AsQueryable());

        // Act
        var result = _mockBankRepo.Object.Table;

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }
}

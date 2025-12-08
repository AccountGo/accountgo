using Core.Data;
using Core.Domain;
using Core.Domain.Financials;
using Moq;

namespace GoodBooks.ServicesTests.Fixtures;

/// <summary>
/// Mock fixtures for common repository instances used in tests.
/// Provides factory methods for creating consistently configured mocks.
/// </summary>
public static class MockRepositoryFixtures
{
    /// <summary>
    /// Creates a mock repository with an empty table.
    /// </summary>
    public static Mock<IRepository<T>> CreateEmptyRepository<T>() where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(new List<T>().AsQueryable());
        return mock;
    }

    /// <summary>
    /// Creates a mock repository with sample data.
    /// </summary>
    public static Mock<IRepository<T>> CreateRepositoryWithData<T>(List<T> data) where T : BaseEntity
    {
        var mock = new Mock<IRepository<T>>();
        mock.Setup(r => r.Table).Returns(data.AsQueryable());
        return mock;
    }

    /// <summary>
    /// Creates a mock SequenceNumber repository with initial sequences.
    /// </summary>
    public static Mock<IRepository<SequenceNumber>> CreateSequenceNumberRepository(
        params SequenceNumber[] sequences)
    {
        var mock = new Mock<IRepository<SequenceNumber>>();
        var sequenceList = new List<SequenceNumber>(sequences);
        
        mock.Setup(r => r.Table).Returns(() => sequenceList.AsQueryable());
        mock.Setup(r => r.Insert(It.IsAny<SequenceNumber>()))
            .Callback<SequenceNumber>(s => sequenceList.Add(s));
        mock.Setup(r => r.Update(It.IsAny<SequenceNumber>()))
            .Callback<SequenceNumber>(s =>
            {
                var existing = sequenceList.FirstOrDefault(x => x.Id == s.Id);
                if (existing != null)
                {
                    existing.NextNumber = s.NextNumber;
                    existing.Description = s.Description;
                }
            });

        return mock;
    }

    /// <summary>
    /// Creates a mock GeneralLedgerSetting repository.
    /// </summary>
    public static Mock<IRepository<GeneralLedgerSetting>> CreateGeneralLedgerSettingRepository(
        params GeneralLedgerSetting[] settings)
    {
        return CreateRepositoryWithData(new List<GeneralLedgerSetting>(settings));
    }

    /// <summary>
    /// Creates a mock Bank repository.
    /// </summary>
    public static Mock<IRepository<Bank>> CreateBankRepository(params Bank[] banks)
    {
        return CreateRepositoryWithData(new List<Bank>(banks));
    }

    /// <summary>
    /// Creates a mock PaymentTerm repository.
    /// </summary>
    public static Mock<IRepository<PaymentTerm>> CreatePaymentTermRepository(
        params PaymentTerm[] paymentTerms)
    {
        return CreateRepositoryWithData(new List<PaymentTerm>(paymentTerms));
    }

    /// <summary>
    /// Creates sample sequence numbers for testing.
    /// </summary>
    public static SequenceNumber CreateSampleSequence(
        int id = 1,
        SequenceNumberTypes type = SequenceNumberTypes.JournalEntry,
        int nextNumber = 1,
        string description = "Test Sequence")
    {
        return new SequenceNumber
        {
            Id = id,
            SequenceNumberType = type,
            NextNumber = nextNumber,
            Description = description,
            UsePrefix = false
        };
    }

    /// <summary>
    /// Creates sample banks for testing.
    /// </summary>
    public static Bank CreateSampleBank(int id = 1, string name = "Test Bank")
    {
        return new Bank
        {
            Id = id,
            BankName = name
        };
    }

    /// <summary>
    /// Creates sample GL settings for testing.
    /// </summary>
    public static GeneralLedgerSetting CreateSampleGeneralLedgerSetting(int id = 1)
    {
        return new GeneralLedgerSetting
        {
            Id = id
        };
    }
}

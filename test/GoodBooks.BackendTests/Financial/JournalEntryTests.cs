using Dto.Financial;
using Xunit;

namespace GoodBooks.BackendTests.Financial;

public class JournalEntryTests
{
    [Fact]
    public void JournalEntryLines_DefaultsToEmptyList()
    {
        var journalEntry = new JournalEntry();

        Assert.NotNull(journalEntry.JournalEntryLines);
        Assert.Empty(journalEntry.JournalEntryLines);
    }

    [Fact]
    public void DebitAmount_SumsDebitLinesOnly()
    {
        var journalEntry = new JournalEntry
        {
            JournalEntryLines =
            {
                new JournalEntryLine { DrCr = 1, Amount = 50m },
                new JournalEntryLine { DrCr = 2, Amount = 75m },
                new JournalEntryLine { DrCr = 1, Amount = null } // null amounts should be treated as zero
            }
        };

        Assert.Equal(50m, journalEntry.debitAmount);
    }

    [Fact]
    public void CreditAmount_SumsCreditLinesOnly()
    {
        var journalEntry = new JournalEntry
        {
            JournalEntryLines =
            {
                new JournalEntryLine { DrCr = 2, Amount = 5m },
                new JournalEntryLine { DrCr = 2, Amount = 10m },
                new JournalEntryLine { DrCr = 1, Amount = 3m }
            }
        };

        Assert.Equal(15m, journalEntry.creditAmount);
    }
}

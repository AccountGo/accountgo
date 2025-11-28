using Dto.Financial;
using Xunit;

namespace GoodBooks.BackendTests.Financial;

public class AccountTests
{
    [Fact]
    public void ChildAccounts_DefaultsToEmptyList()
    {
        var account = new Account();

        Assert.NotNull(account.ChildAccounts);
        Assert.Empty(account.ChildAccounts);
    }

    [Fact]
    public void TotalBalance_SumsAllDescendantBalances()
    {
        var account = new Account
        {
            Balance = 100m, // root balance is intentionally ignored by the DTO logic
            ChildAccounts =
            {
                new Account { Balance = 50m },
                new Account
                {
                    Balance = 25m,
                    ChildAccounts =
                    {
                        new Account { Balance = 10m },
                        new Account { Balance = 5m }
                    }
                }
            }
        };

        Assert.Equal(90m, account.TotalBalance);
    }

    [Fact]
    public void TotalDebitAndCreditBalance_SumAllDescendants()
    {
        var account = new Account
        {
            ChildAccounts =
            {
                new Account { DebitBalance = 40m, CreditBalance = 5m },
                new Account
                {
                    DebitBalance = 10m,
                    CreditBalance = 20m,
                    ChildAccounts =
                    {
                        new Account { DebitBalance = 2m, CreditBalance = 3m }
                    }
                }
            }
        };

        Assert.Equal(52m, account.TotalDebitBalance);
        Assert.Equal(28m, account.TotalCreditBalance);
    }
}
